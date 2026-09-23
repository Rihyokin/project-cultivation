using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tripo3D.UnityBridge.Editor
{
    /// <summary>
    /// Handles model import and scene placement
    /// </summary>
    public class ModelImporter
    {
        public event Action<float> OnProgressUpdate;

        private const string IMPORT_FOLDER = "Assets/TripoModels";
        private const int MAX_CLEANUP_RETRIES = 5;
        private static readonly char[] KEY_TRIM_CHARS = { '_', '-', ' ', '.' };
        private static readonly string[] TEXTURE_SUFFIXES =
        {
            "_basecolor", "basecolor", "_base_color", "base_color",
            "_basecolor_map", "basecolor_map", "_base_color_map", "base_color_map",
            "_basecolormap", "basecolormap", "_basecolortex", "basecolortex",
            "_albedo", "albedo", "_albedo_map", "albedo_map", "_albedomap", "albedomap",
            "_diffuse", "diffuse", "_diffuse_color", "diffuse_color",
            "_diffuse_map", "diffuse_map", "_diffusemap", "diffusemap",
            "_smoothness", "smoothness",
            "_smoothness_map", "smoothness_map", "_smoothnessmap", "smoothnessmap",
            "_metallic", "metallic", "_metalness", "metalness",
            "_metallic_map", "metallic_map", "_metallicmap", "metallicmap",
            "_metalness_map", "metalness_map", "_metalnessmap", "metalnessmap",
            "_roughness", "roughness",
            "_roughness_map", "roughness_map", "_roughnessmap", "roughnessmap",
            "_normal", "normal", "_normalgl", "normalgl", "_normaldx", "normaldx",
            "_normal_map", "normal_map", "_normalmap", "normalmap"
        };
        private static readonly string[] MATERIAL_PREFIXES = { "material_", "material-", "material.", "mat_", "mat-", "mat." };
        private static readonly string[] MATERIAL_SUFFIXES = { "_material", "-material", ".material", "_mat", "-mat", ".mat" };

        /// <summary>
        /// Import model from file data
        /// </summary>
        public bool ImportModel(string fileId, string fileName, string fileType, byte[] fileData)
        {
            try
            {
                LogHelper.Log("Starting import process...");
                OnProgressUpdate?.Invoke(0f);

                // Determine if ZIP or direct model file
                bool isZip = fileType.ToLower() == "zip" || fileName.ToLower().EndsWith(".zip");
                
                string tempPath = Path.Combine(UnityEngine.Application.temporaryCachePath, fileId);
                Directory.CreateDirectory(tempPath);

                string modelPath;
                
                if (isZip)
                {
                    // Handle ZIP file
                    modelPath = ProcessZipFile(fileId, fileName, fileData, tempPath);
                }
                else
                {
                    // Handle direct FBX/OBJ file
                    modelPath = ProcessDirectFile(fileId, fileName, fileType, fileData, tempPath);
                }

                if (string.IsNullOrEmpty(modelPath))
                {
                    LogHelper.Log("Error: Could not locate model file");
                    CleanupTempDirectory(tempPath);
                    return false;
                }

                OnProgressUpdate?.Invoke(0.5f);

                // Determine unique model name (used for folder, fbx file, and scene instance)
                string modelName = GetCleanModelName(fileName);
                string uniqueModelName = GetUniqueModelName(modelName);

                // Rename the model file in temp to the unique model name
                string modelExt = Path.GetExtension(modelPath);
                string modelDir = Path.GetDirectoryName(modelPath);
                string originalBaseName = Path.GetFileNameWithoutExtension(modelPath);
                string renamedModelPath = Path.Combine(modelDir, uniqueModelName + modelExt);
                if (!string.Equals(modelPath, renamedModelPath, StringComparison.OrdinalIgnoreCase))
                    File.Move(modelPath, renamedModelPath);
                modelPath = renamedModelPath;

                // Also rename the associated .fbm texture folder if it exists
                string fbmFolderOld = Path.Combine(modelDir, originalBaseName + ".fbm");
                string fbmFolderNew = Path.Combine(modelDir, uniqueModelName + ".fbm");
                if (Directory.Exists(fbmFolderOld) && !string.Equals(fbmFolderOld, fbmFolderNew, StringComparison.OrdinalIgnoreCase))
                    Directory.Move(fbmFolderOld, fbmFolderNew);

                // Move to Assets folder
                string assetPath = MoveToAssetsFolder(uniqueModelName, tempPath);
                if (string.IsNullOrEmpty(assetPath))
                {
                    LogHelper.Log("Error: Failed to move files to Assets folder");
                    CleanupTempDirectory(tempPath);
                    return false;
                }

                OnProgressUpdate?.Invoke(0.7f);

                // Import asset
                bool imported = ImportAsset(assetPath, Path.GetFileName(modelPath), modelName, originalBaseName);
                if (!imported)
                {
                    LogHelper.Log("Error: Asset import failed");
                    return false;
                }

                OnProgressUpdate?.Invoke(0.9f);

                // Add to scene
                bool addedToScene = AddToScene(assetPath, Path.GetFileName(modelPath), uniqueModelName);
                
                OnProgressUpdate?.Invoke(1f);

                // Cleanup temp directory
                CleanupTempDirectory(tempPath);

                return addedToScene;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Import error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Process ZIP file - extract and find model
        /// </summary>
        private string ProcessZipFile(string fileId, string fileName, byte[] fileData, string tempPath)
        {
            try
            {
                LogHelper.Log("Extracting ZIP file...");
                
                // Save ZIP to temp
                string zipPath = Path.Combine(tempPath, "archive.zip");
                File.WriteAllBytes(zipPath, fileData);

                // Extract ZIP
                string extractPath = Path.Combine(tempPath, "extracted");
                Directory.CreateDirectory(extractPath);
                ZipFile.ExtractToDirectory(zipPath, extractPath);

                File.Delete(zipPath);

                // Find FBX or OBJ file
                string modelFile = FindModelFile(extractPath);
                
                if (string.IsNullOrEmpty(modelFile))
                {
                    LogHelper.Log("Error: No FBX or OBJ file found in ZIP");
                    return null;
                }

                LogHelper.Verbose($"Found model: {Path.GetFileName(modelFile)}");
                
                // Move extracted files to root temp path
                MoveDirectory(extractPath, tempPath);
                Directory.Delete(extractPath, true);

                return Path.Combine(tempPath, Path.GetFileName(modelFile));
            }
            catch (Exception ex)
            {
                LogHelper.Error($"ZIP extraction error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Process direct model file (FBX/OBJ)
        /// </summary>
        private string ProcessDirectFile(string fileId, string fileName, string fileType, byte[] fileData, string tempPath)
        {
            try
            {
                LogHelper.Log($"Processing {fileType.ToUpper()} file...");
                
                string extension = fileType.StartsWith(".") ? fileType : $".{fileType}";
                string modelPath = Path.Combine(tempPath, $"{fileName}{extension}");
                
                File.WriteAllBytes(modelPath, fileData);
                
                return modelPath;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"File save error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Find FBX or OBJ file in directory (recursively)
        /// </summary>
        private string FindModelFile(string directory)
        {
            // Search for FBX first
            var fbxFiles = Directory.GetFiles(directory, "*.fbx", SearchOption.AllDirectories);
            if (fbxFiles.Length > 0)
                return fbxFiles[0];

            // Then search for OBJ
            var objFiles = Directory.GetFiles(directory, "*.obj", SearchOption.AllDirectories);
            if (objFiles.Length > 0)
                return objFiles[0];

            return null;
        }

        /// <summary>
        /// Move directory contents
        /// </summary>
        private void MoveDirectory(string source, string target)
        {
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relativePath = file.Substring(source.Length + 1);
                string targetPath = Path.Combine(target, relativePath);
                
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                File.Move(file, targetPath);
            }
        }

        /// <summary>
        /// Strip known model/archive extensions to get a clean display name
        /// </summary>
        private static string GetCleanModelName(string fileName)
        {
            string name = fileName ?? "model";
            // Strip known extensions
            foreach (var ext in new[] { ".zip", ".fbx", ".obj", ".glb", ".gltf" })
            {
                if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - ext.Length);
                    break;
                }
            }
            return string.IsNullOrWhiteSpace(name) ? "model" : name.Replace(' ', '_');
        }

        /// <summary>
        /// Return a unique model name by appending a number if the Assets folder already exists
        /// </summary>
        private static string GetUniqueModelName(string baseName)
        {
            string candidate = baseName;
            string folderPath = Path.Combine(IMPORT_FOLDER, candidate);
            if (!Directory.Exists(folderPath))
                return candidate;

            int index = 1;
            while (true)
            {
                candidate = $"{baseName}_{index}";
                folderPath = Path.Combine(IMPORT_FOLDER, candidate);
                if (!Directory.Exists(folderPath))
                    return candidate;
                index++;
            }
        }

        private string MoveToAssetsFolder(string modelName, string tempPath)
        {
            try
            {
                LogHelper.Log("Moving to Assets folder...");
                
                // Create import folder if needed
                if (!Directory.Exists(IMPORT_FOLDER))
                {
                    Directory.CreateDirectory(IMPORT_FOLDER);
                }

                // Create subfolder named after the model
                string assetFolder = Path.Combine(IMPORT_FOLDER, modelName);
                Directory.CreateDirectory(assetFolder);

                // Copy all files
                foreach (var file in Directory.GetFiles(tempPath, "*", SearchOption.AllDirectories))
                {
                    string relativePath = file.Substring(tempPath.Length + 1);
                    string targetPath = Path.Combine(assetFolder, relativePath);
                    
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                    File.Copy(file, targetPath, true);
                }

                AssetDatabase.Refresh();
                
                return assetFolder;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Move error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Import asset with Unity's AssetDatabase
        /// </summary>
        private bool ImportAsset(string assetPath, string modelFileName, params string[] sourceModelNames)
        {
            try
            {
                LogHelper.Log("Importing asset...");
                
                string modelAssetPath = Path.Combine(assetPath, modelFileName).Replace("\\", "/");
                
                // Fix normal maps BEFORE importing materials (to avoid warnings)
                FixNormalMaps(assetPath);
                
                // Combine Metallic and Roughness textures into Unity format
                CombineMetallicRoughnessTextures(assetPath);
                
                AssetDatabase.StartAssetEditing();
                try
                {
                    AssetDatabase.ImportAsset(modelAssetPath, ImportAssetOptions.ForceUpdate);
                    
                    // Configure model importer
                    var importer = AssetImporter.GetAtPath(modelAssetPath) as UnityEditor.ModelImporter;
                    if (importer != null)
                    {
                        importer.materialImportMode = UnityEditor.ModelImporterMaterialImportMode.ImportStandard;
                        // NOTE: ModelImporterMaterialLocation.External is obsolete in Unity 6 and no
                        // longer supported. Materials are imported EMBEDDED, then extracted to writable
                        // .mat assets below via AssetDatabase.ExtractAsset (the supported replacement).
                        importer.SaveAndReimport();
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                
                AssetDatabase.Refresh();

                // Extract the embedded materials to writable .mat files on disk.
                // (ModelImporterMaterialLocation.External is obsolete in Unity 6, so this
                //  is done explicitly. ExtractAsset also records the model->.mat remap in
                //  the .meta, keyed by GUID, so the model keeps referencing our .mat.)
                ExtractEmbeddedMaterials(modelAssetPath, assetPath);

                // Apply textures to materials AFTER extraction.
                // Must run BEFORE renaming: texture<->material matching relies on the
                // material name still carrying the part key (e.g. "mesh_basecolor").
                ApplyTexturesToMaterials(assetPath, modelAssetPath);

                // Rename the extracted materials to the model name.
                // Done LAST so the rename does not break the texture matching above.
                // The .mat GUID is unchanged by a rename, so the model's remap still resolves.
                string finalModelName = Path.GetFileNameWithoutExtension(modelFileName);
                RenameMaterialsToModelName(assetPath, modelAssetPath, finalModelName);

                // Apply the final unique model prefix to every imported texture, including source
                // maps that are not assigned to a Unity material (e.g. metallic, rm, roughness).
                RenameImportedTexturePrefixes(assetPath, finalModelName, sourceModelNames);

                // Keep material-assigned texture asset names aligned with final material names.
                RenameAssignedTexturesToMaterialNames(assetPath);
                
                LogHelper.Log("Asset imported successfully");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Import error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Extract embedded materials from the model into writable .mat files.
        /// Replaces the obsolete ModelImporterMaterialLocation.External. ExtractAsset moves
        /// each embedded material onto disk and records a GUID-based remap in the model's
        /// .meta, so the model keeps referencing the extracted asset across reimports.
        /// </summary>
        private void ExtractEmbeddedMaterials(string modelAssetPath, string assetPath)
        {
            try
            {
                var materials = AssetDatabase.LoadAllAssetsAtPath(modelAssetPath)
                    .OfType<Material>()
                    .ToList();
                int extracted = 0;

                // Nothing to extract: return early so we don't leave an empty Materials folder.
                if (materials.Count == 0)
                {
                    LogHelper.Verbose("No embedded materials to extract");
                    return;
                }

                // Put materials in a "Materials" subfolder, matching Unity's classic
                // External-location convention (e.g. Assets/TripoModels/{model}/Materials/).
                string materialsFolder = Path.Combine(assetPath, "Materials").Replace("\\", "/");
                if (!AssetDatabase.IsValidFolder(materialsFolder))
                    AssetDatabase.CreateFolder(assetPath, "Materials");

                // During StartAssetEditing, .mat files already produced by ExtractAsset are not yet
                // registered in the AssetDatabase, so GenerateUniqueAssetPath can't see them. Two
                // identically named embedded materials would resolve to the same path and clobber
                // each other. Track paths used in this batch to guarantee uniqueness.
                var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (var material in materials)
                    {
                        // Target path: {model}/Materials/{name}.mat, keeping the embedded name for now
                        string destPath = Path.Combine(materialsFolder, material.name + ".mat").Replace("\\", "/");
                        destPath = GetUniqueBatchPath(destPath, usedPaths);

                        string error = AssetDatabase.ExtractAsset(material, destPath);
                        if (string.IsNullOrEmpty(error))
                            extracted++;
                        else
                            LogHelper.Warning($"ExtractAsset failed for '{material.name}': {error}");
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                if (extracted > 0)
                {
                    // Reimport the model so its material references rebind to the extracted .mat files
                    AssetDatabase.ImportAsset(modelAssetPath, ImportAssetOptions.ForceUpdate);
                    AssetDatabase.Refresh();
                    LogHelper.Verbose($"Extracted {extracted} embedded material(s) to disk");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Material extraction error: {ex.Message}");
            }
        }

        /// <summary>
        /// Produce a unique asset path inside a StartAssetEditing batch.
        /// GenerateUniqueAssetPath handles assets already on disk; usedPaths handles
        /// not-yet-registered siblings from the same batch, appending _1/_2... on collision.
        /// </summary>
        private static string GetUniqueBatchPath(string destPath, HashSet<string> usedPaths)
        {
            destPath = AssetDatabase.GenerateUniqueAssetPath(destPath);

            if (usedPaths.Add(destPath))
                return destPath;

            string directory = Path.GetDirectoryName(destPath).Replace("\\", "/");
            string nameWithoutExt = Path.GetFileNameWithoutExtension(destPath);
            string extension = Path.GetExtension(destPath);

            int suffix = 1;
            string candidate;
            do
            {
                candidate = Path.Combine(directory, $"{nameWithoutExt}_{suffix}{extension}").Replace("\\", "/");
                candidate = AssetDatabase.GenerateUniqueAssetPath(candidate);
                suffix++;
            }
            while (!usedPaths.Add(candidate));

            return candidate;
        }

        /// <summary>
        /// Rename the extracted .mat files so their names match the model name.
        /// Single material  -> "{modelName}"
        /// Multiple material -> "{modelName}_0", "{modelName}_1", ...
        /// RenameAsset preserves the asset GUID, so the model's material remap (recorded by
        /// ExtractAsset) keeps resolving and no duplicate material is regenerated.
        /// </summary>
        private void RenameMaterialsToModelName(string assetPath, string modelAssetPath, string modelName)
        {
            try
            {
                // Scan the extracted .mat files on disk (produced by ExtractEmbeddedMaterials).
                string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { assetPath });
                if (materialGuids == null || materialGuids.Length == 0)
                {
                    LogHelper.Verbose("No materials to rename");
                    return;
                }

                // Deterministic ordering so multi-material suffixes are stable across imports
                var materialPaths = materialGuids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();
                CollectMaterialContext(modelAssetPath, materialPaths, out List<string> orderedMaterialPaths);
                materialPaths = orderedMaterialPaths;

                bool single = materialPaths.Count == 1;
                int index = 0;

                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string materialPath in materialPaths)
                    {
                        string targetName = single ? modelName : $"{modelName}_{index}";
                        index++;

                        if (string.Equals(Path.GetFileNameWithoutExtension(materialPath), targetName, StringComparison.Ordinal))
                            continue; // already correctly named

                        // RenameAsset keeps the GUID, so the model's material remap (set by
                        // ExtractAsset) still resolves - no duplicate is regenerated.
                        string renameError = AssetDatabase.RenameAsset(materialPath, targetName);
                        if (!string.IsNullOrEmpty(renameError))
                            LogHelper.Warning($"Rename material failed ({materialPath}): {renameError}");
                        else
                            LogHelper.Verbose($"Renamed material -> {targetName}");
                    }
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }

                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Material rename error: {ex.Message}");
            }
        }

        private void RenameAssignedTexturesToMaterialNames(string assetPath)
        {
            try
            {
                string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { assetPath });
                if (materialGuids == null || materialGuids.Length == 0)
                    return;

                int renamedCount = 0;
                var renamedTextureIds = new HashSet<string>();

                foreach (string guid in materialGuids)
                {
                    string materialPath = AssetDatabase.GUIDToAssetPath(guid);
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                        continue;

                    renamedCount += RenameMaterialTexture(material, assetPath, renamedTextureIds,
                        new[] { "_MainTex", "_BaseMap", "_BaseColorMap" }, "_BaseColor");
                    renamedCount += RenameMaterialTexture(material, assetPath, renamedTextureIds,
                        new[] { "_MetallicGlossMap", "_MaskMap" }, "_MetallicSmoothness");
                    renamedCount += RenameMaterialTexture(material, assetPath, renamedTextureIds,
                        new[] { "_BumpMap", "_NormalMap" }, "_Normal");
                }

                if (renamedCount > 0)
                {
                    AssetDatabase.Refresh();
                    LogHelper.Verbose($"Renamed {renamedCount} material texture asset(s)");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Texture rename error: {ex.Message}");
            }
        }

        private int RenameMaterialTexture(Material material, string assetPath, HashSet<string> renamedTextureIds, string[] propertyNames, string suffix)
        {
            foreach (string propertyName in propertyNames)
            {
                if (!material.HasProperty(propertyName))
                    continue;

                Texture texture = material.GetTexture(propertyName);
                if (texture == null)
                    continue;

                string textureTrackingId = GetTextureTrackingId(texture);
                if (renamedTextureIds.Contains(textureTrackingId))
                    continue;

                string texturePath = AssetDatabase.GetAssetPath(texture);
                if (string.IsNullOrEmpty(texturePath) ||
                    !(texturePath.Equals(assetPath, StringComparison.OrdinalIgnoreCase) ||
                      texturePath.StartsWith(assetPath + "/", StringComparison.OrdinalIgnoreCase)))
                    continue;

                string targetName = material.name + suffix;
                string currentName = Path.GetFileNameWithoutExtension(texturePath);
                if (string.Equals(currentName, targetName, StringComparison.Ordinal))
                {
                    renamedTextureIds.Add(textureTrackingId);
                    return 0;
                }

                string textureDirectory = Path.GetDirectoryName(texturePath);
                string textureExtension = Path.GetExtension(texturePath);
                string targetPath = Path.Combine(textureDirectory, targetName + textureExtension).Replace("\\", "/");
                string renameError = RenameAssetToPath(texturePath, targetPath, out targetPath);

                if (!string.IsNullOrEmpty(renameError))
                    LogHelper.Warning($"Rename texture failed ({texturePath}): {renameError}");
                else
                    LogHelper.Verbose($"Renamed texture -> {Path.GetFileNameWithoutExtension(targetPath)}");

                renamedTextureIds.Add(textureTrackingId);
                return string.IsNullOrEmpty(renameError) ? 1 : 0;
            }

            return 0;
        }

        private void RenameImportedTexturePrefixes(string assetPath, string finalModelName, params string[] sourceModelNames)
        {
            try
            {
                var sourcePrefixes = sourceModelNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(name => name.Length)
                    .ToList();
                if (sourcePrefixes.Count == 0)
                    return;

                var texturePaths = AssetDatabase.FindAssets("t:Texture", new[] { assetPath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();

                int renamedCount = 0;
                foreach (string texturePath in texturePaths)
                {
                    string currentName = Path.GetFileNameWithoutExtension(texturePath);
                    if (HasModelNamePrefix(currentName, finalModelName))
                        continue;

                    string sourcePrefix = sourcePrefixes.FirstOrDefault(prefix => HasModelNamePrefix(currentName, prefix));
                    if (sourcePrefix == null)
                        continue;

                    string targetName = finalModelName + currentName.Substring(sourcePrefix.Length);
                    string targetPath = Path.Combine(
                        Path.GetDirectoryName(texturePath),
                        targetName + Path.GetExtension(texturePath)).Replace("\\", "/");
                    string renameError = RenameAssetToPath(texturePath, targetPath, out targetPath);

                    if (!string.IsNullOrEmpty(renameError))
                    {
                        LogHelper.Warning($"Rename texture prefix failed ({texturePath}): {renameError}");
                        continue;
                    }

                    renamedCount++;
                    LogHelper.Verbose($"Renamed texture prefix -> {Path.GetFileNameWithoutExtension(targetPath)}");
                }

                if (renamedCount > 0)
                {
                    AssetDatabase.Refresh();
                    LogHelper.Verbose($"Renamed {renamedCount} imported texture prefix(es)");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Texture prefix rename error: {ex.Message}");
            }
        }

        private static bool HasModelNamePrefix(string assetName, string modelName)
        {
            if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(modelName) ||
                !assetName.StartsWith(modelName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (assetName.Length == modelName.Length)
                return true;

            char separator = assetName[modelName.Length];
            return separator == '_' || separator == '-' || separator == '.' || separator == ' ';
        }

        private static string RenameAssetToPath(string sourcePath, string targetPath, out string actualTargetPath)
        {
            actualTargetPath = targetPath;
            if (string.Equals(sourcePath, targetPath, StringComparison.Ordinal))
                return string.Empty;

            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase) &&
                !TargetPathBelongsToAnotherAsset(sourcePath, targetPath))
                return RenameAssetCaseOnly(sourcePath, targetPath);

            actualTargetPath = AssetDatabase.GenerateUniqueAssetPath(targetPath);
            return AssetDatabase.RenameAsset(sourcePath, Path.GetFileNameWithoutExtension(actualTargetPath));
        }

        private static bool TargetPathBelongsToAnotherAsset(string sourcePath, string targetPath)
        {
            string targetGuid = AssetDatabase.AssetPathToGUID(targetPath);
            if (string.IsNullOrEmpty(targetGuid))
                return false;

            string sourceGuid = AssetDatabase.AssetPathToGUID(sourcePath);
            return !string.Equals(sourceGuid, targetGuid, StringComparison.OrdinalIgnoreCase);
        }

        private static string RenameAssetCaseOnly(string sourcePath, string targetPath)
        {
            string directory = Path.GetDirectoryName(sourcePath);
            string extension = Path.GetExtension(sourcePath);
            string sourceName = Path.GetFileNameWithoutExtension(sourcePath);
            string targetName = Path.GetFileNameWithoutExtension(targetPath);
            string temporaryPath = Path.Combine(directory, sourceName + "__TripoRenameTemp" + extension).Replace("\\", "/");
            temporaryPath = AssetDatabase.GenerateUniqueAssetPath(temporaryPath);

            string temporaryName = Path.GetFileNameWithoutExtension(temporaryPath);
            string renameError = AssetDatabase.RenameAsset(sourcePath, temporaryName);
            if (!string.IsNullOrEmpty(renameError))
                return renameError;

            // Older AssetDatabase versions can keep the source path occupied until refreshed.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            renameError = AssetDatabase.RenameAsset(temporaryPath, targetName);
            if (string.IsNullOrEmpty(renameError))
                return string.Empty;

            string rollbackError = AssetDatabase.RenameAsset(temporaryPath, sourceName);
            if (!string.IsNullOrEmpty(rollbackError))
                return $"{renameError}; rollback failed: {rollbackError}";

            return renameError;
        }

        private string GetTextureTrackingId(Texture texture)
        {
            return UnityCompat.GetTrackingId(texture);
        }

        /// <summary>
        /// Combine Metallic and Roughness textures into Unity's Metallic/Smoothness format
        /// Unity uses: R channel = Metallic, A channel = Smoothness (1 - Roughness)
        /// Supports multi-part models with separate textures per part
        /// </summary>
        private void CombineMetallicRoughnessTextures(string assetPath)
        {
            try
            {
                LogHelper.Verbose("Checking for Metallic/Roughness textures to combine...");
                
                // Find all textures in the import folder
                string[] textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { assetPath });
                
                // Group textures by part name
                var metallicTextures = new Dictionary<string, string>();
                var roughnessTextures = new Dictionary<string, string>();
                
                // Find and categorize metallic and roughness textures
                foreach (string guid in textureGuids)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(texturePath).ToLower();
                    
                    // Check for metallic texture
                    if (ContainsTextureToken(fileNameWithoutExt, "metallic", "metalness"))
                    {
                        string partKey = NormalizePartKey(fileNameWithoutExt);
                        metallicTextures[partKey] = texturePath;
                    }
                    
                    // Check for roughness texture
                    if (ContainsTextureToken(fileNameWithoutExt, "roughness"))
                    {
                        string partKey = NormalizePartKey(fileNameWithoutExt);
                        roughnessTextures[partKey] = texturePath;
                    }
                }
                
                // Combine matching pairs
                int combinedCount = 0;
                foreach (var metallicEntry in metallicTextures)
                {
                    string partKey = metallicEntry.Key;
                    string metallicPath = metallicEntry.Value;
                    
                    if (roughnessTextures.ContainsKey(partKey))
                    {
                        string roughnessPath = roughnessTextures[partKey];
                        
                        LogHelper.Verbose($"Combining textures for part: {partKey}");
                        
                        // Load textures as readable
                        Texture2D metallicTex = LoadTextureReadable(metallicPath);
                        Texture2D roughnessTex = LoadTextureReadable(roughnessPath);
                        
                        if (metallicTex != null && roughnessTex != null)
                        {
                            // Create combined texture
                            int width = Mathf.Max(metallicTex.width, roughnessTex.width);
                            int height = Mathf.Max(metallicTex.height, roughnessTex.height);
                            Texture2D combined = new Texture2D(width, height, TextureFormat.RGBA32, true);
                            
                            for (int y = 0; y < height; y++)
                            {
                                for (int x = 0; x < width; x++)
                                {
                                    // Sample textures (with UV scaling if sizes differ)
                                    float u = (float)x / width;
                                    float v = (float)y / height;
                                    
                                    Color metallic = metallicTex.GetPixelBilinear(u, v);
                                    Color roughness = roughnessTex.GetPixelBilinear(u, v);
                                    
                                    // Unity format: R = Metallic, G & B = 0, A = Smoothness (1 - Roughness)
                                    float metallicValue = metallic.r;  // Usually grayscale
                                    float smoothness = 1f - roughness.r;  // Convert roughness to smoothness
                                    
                                    combined.SetPixel(x, y, new Color(metallicValue, metallicValue, metallicValue, smoothness));
                                }
                            }
                            
                            combined.Apply();
                            
                            // Save combined texture with original part name
                            string baseFileName = Path.GetFileNameWithoutExtension(metallicPath);
                            string combinedPath = Path.Combine(Path.GetDirectoryName(metallicPath), 
                                baseFileName + "_Smoothness.png");
                            byte[] pngData = combined.EncodeToPNG();
                            File.WriteAllBytes(combinedPath, pngData);
                            
                            LogHelper.Verbose($"Created: {Path.GetFileName(combinedPath)}");
                            combinedCount++;
                            
                            // Clean up
                            UnityEngine.Object.DestroyImmediate(combined);
                        }
                    }
                }
                
                if (combinedCount > 0)
                {
                    AssetDatabase.Refresh();
                    LogHelper.Verbose($"Successfully combined {combinedCount} Metallic/Smoothness texture pair(s)");
                }
                else
                {
                    LogHelper.Verbose("No matching Metallic/Roughness texture pairs found");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Metallic/Roughness combination error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extract part key from texture filename by removing texture type suffix
        /// </summary>
        private string ExtractPartKey(string fileName, string[] suffixes)
        {
            if (string.IsNullOrEmpty(fileName))
                return fileName;

            string key = fileName;
            foreach (string suffix in suffixes)
            {
                if (EndsWithTextureSuffix(key, suffix))
                {
                    key = key.Substring(0, key.Length - suffix.Length);
                    break;
                }
            }
            return key;
        }

        private bool EndsWithTextureSuffix(string key, string suffix)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(suffix) ||
                !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return false;

            int suffixStart = key.Length - suffix.Length;
            if (suffixStart == 0 || !char.IsLetterOrDigit(key[suffixStart - 1]) || !char.IsLetterOrDigit(suffix[0]))
                return true;

            // Avoid turning ordinary words like "abnormal" into "ab" while still allowing
            // compact texture names such as "meshbasecolor" and "partmetallic".
            return !IsAmbiguousTextureSuffix(suffix);
        }

        private bool IsAmbiguousTextureSuffix(string suffix)
        {
            switch (suffix.TrimStart('_').ToLower())
            {
                case "normal":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Normalize part key by removing known suffixes and fixing "part_new" naming.
        /// </summary>
        private string NormalizePartKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            string normalized = key.ToLower();

            // Remove all stacked texture suffixes, e.g. "part_metallic_smoothness" -> "part".
            string previous;
            do
            {
                previous = normalized;
                normalized = ExtractPartKey(normalized, TEXTURE_SUFFIXES).Trim(KEY_TRIM_CHARS);
            }
            while (!string.Equals(previous, normalized, StringComparison.Ordinal));

            // Trim trailing separators left behind by suffix stripping so that the material-side
            // key ("parta") and the texture-side key ("parta_") normalize to the SAME value.
            // Without this, ExtractPartKey (used on the texture side) leaves "parta_" while the
            // material side yields "parta", and the exact-match in FindMatchingTexture never hits
            // for multi-material models.
            normalized = normalized.Trim(KEY_TRIM_CHARS);

            // If normalization resulted in empty string, fall back to original key
            if (string.IsNullOrEmpty(normalized))
                return key.ToLower();

            return normalized;
        }
        
        /// <summary>
        /// Find texture that best matches the material name
        /// </summary>
        private string FindMatchingTexture(List<string> candidateKeys, Dictionary<string, string> textures)
        {
            foreach (string key in candidateKeys)
            {
                if (textures.ContainsKey(key))
                    return textures[key];
            }
            
            // If only one texture exists, use it (single-material models)
            if (textures.Count == 1)
            {
                using (var enumerator = textures.Values.GetEnumerator())
                {
                    if (enumerator.MoveNext())
                    {
                        return enumerator.Current;
                    }
                }
            }
            
            return null;
        }

        private void AddTextureLookupKeys(Dictionary<string, string> textures, string fileNameWithoutExt, string modelKey, List<string> modelPrefixKeys, string texturePath)
        {
            string partKey = NormalizePartKey(fileNameWithoutExt);
            AddTextureLookupKey(textures, partKey, texturePath);

            foreach (string prefixKey in modelPrefixKeys)
            {
                string modelPrefix = prefixKey.Trim(KEY_TRIM_CHARS) + "_";
                if (!partKey.StartsWith(modelPrefix, StringComparison.Ordinal))
                    continue;

                string keyWithoutModel = partKey.Substring(modelPrefix.Length).Trim(KEY_TRIM_CHARS);
                AddTextureLookupKey(textures, keyWithoutModel, texturePath);

                string partNumber = GetTrailingNumber(keyWithoutModel);
                if (!string.IsNullOrEmpty(partNumber))
                {
                    AddTextureLookupKey(textures, $"{modelKey}_{partNumber}", texturePath);
                    AddTextureLookupKey(textures, $"{prefixKey}_{partNumber}", texturePath);
                }
            }
        }

        private void AddTextureLookupKey(Dictionary<string, string> textures, string key, string texturePath)
        {
            if (string.IsNullOrEmpty(key))
                return;

            if (!textures.ContainsKey(key))
                textures[key] = texturePath;
        }

        private string GetTrailingNumber(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            int index = key.Length - 1;
            while (index >= 0 && char.IsDigit(key[index]))
                index--;

            if (index == key.Length - 1)
                return null;

            if (index >= 0 && (key[index] == '_' || key[index] == '-' || key[index] == '.' || key[index] == ' '))
                return key.Substring(index + 1);

            return null;
        }

        private string CompactKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            char[] chars = key.Where(char.IsLetterOrDigit).ToArray();
            return new string(chars);
        }

        private bool ContainsTextureToken(string fileNameWithoutExtension, params string[] tokens)
        {
            string compactFileName = CompactKey(fileNameWithoutExtension);
            return tokens.Any(token => compactFileName.Contains(CompactKey(token)));
        }

        private Dictionary<string, List<string>> CollectMaterialContext(string modelAssetPath, List<string> materialPaths, out List<string> orderedMaterialPaths)
        {
            var result = new Dictionary<string, List<string>>();
            orderedMaterialPaths = new List<string>();
            var knownPaths = new HashSet<string>(materialPaths);

            GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
            if (modelPrefab == null)
            {
                orderedMaterialPaths.AddRange(materialPaths);
                return result;
            }

            foreach (Renderer renderer in modelPrefab.GetComponentsInChildren<Renderer>(true))
            {
                var rendererKeys = new List<string>();
                AddCandidateKey(rendererKeys, renderer.name);
                AddCandidateKey(rendererKeys, renderer.gameObject.name);

                Mesh sharedMesh = null;
                if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
                    sharedMesh = skinnedMeshRenderer.sharedMesh;
                else
                    sharedMesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;

                if (sharedMesh != null)
                    AddCandidateKey(rendererKeys, sharedMesh.name);

                foreach (Material sharedMaterial in renderer.sharedMaterials)
                {
                    if (sharedMaterial == null)
                        continue;

                    string materialPath = AssetDatabase.GetAssetPath(sharedMaterial);
                    if (string.IsNullOrEmpty(materialPath))
                        continue;

                    if (knownPaths.Contains(materialPath) && !orderedMaterialPaths.Contains(materialPath))
                        orderedMaterialPaths.Add(materialPath);

                    if (!result.TryGetValue(materialPath, out List<string> materialKeys))
                    {
                        materialKeys = new List<string>();
                        result[materialPath] = materialKeys;
                    }

                    AddCandidateKey(materialKeys, sharedMaterial.name);
                    foreach (string rendererKey in rendererKeys)
                        AddCandidateKey(materialKeys, rendererKey);
                }
            }

            foreach (string materialPath in materialPaths)
            {
                if (!orderedMaterialPaths.Contains(materialPath))
                    orderedMaterialPaths.Add(materialPath);
            }

            return result;
        }

        private void AddCandidateKey(List<string> keys, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            AddUniqueKey(keys, value.ToLower().Trim(KEY_TRIM_CHARS));

            foreach (string candidate in GetCandidateKeyVariants(value))
            {
                if (!string.IsNullOrEmpty(candidate) && !keys.Contains(candidate))
                    keys.Add(candidate);
            }
        }

        private List<string> GetCandidateKeyVariants(string value)
        {
            var variants = new List<string>();
            string normalized = NormalizePartKey(value);
            AddUniqueKey(variants, normalized);

            string duplicateSuffixStripped = StripTrailingDuplicateNumber(normalized);
            AddUniqueKey(variants, duplicateSuffixStripped);

            string materialWordStripped = StripMaterialWords(normalized);
            AddUniqueKey(variants, materialWordStripped);
            AddUniqueKey(variants, StripTrailingDuplicateNumber(materialWordStripped));

            return variants;
        }

        private void AddUniqueKey(List<string> keys, string key)
        {
            if (!string.IsNullOrEmpty(key) && !keys.Contains(key))
                keys.Add(key);
        }

        private string StripTrailingDuplicateNumber(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            int index = key.Length - 1;
            while (index >= 0 && char.IsDigit(key[index]))
                index--;

            if (index == key.Length - 1)
                return key;

            if (index >= 0 && (key[index] == '_' || key[index] == '-' || key[index] == '.' || key[index] == ' '))
                return key.Substring(0, index).Trim(KEY_TRIM_CHARS);

            return key;
        }

        private string StripMaterialWords(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;

            string stripped = key;
            foreach (string prefix in MATERIAL_PREFIXES)
            {
                if (stripped.StartsWith(prefix, StringComparison.Ordinal))
                {
                    stripped = stripped.Substring(prefix.Length);
                    break;
                }
            }

            foreach (string suffix in MATERIAL_SUFFIXES)
            {
                if (stripped.EndsWith(suffix, StringComparison.Ordinal))
                {
                    stripped = stripped.Substring(0, stripped.Length - suffix.Length);
                    break;
                }
            }

            return stripped.Trim(KEY_TRIM_CHARS);
        }
        
        /// <summary>
        /// Load a texture as readable (required for GetPixel/SetPixel operations)
        /// </summary>
        private Texture2D LoadTextureReadable(string texturePath)
        {
            try
            {
                // Make texture readable
                TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                if (importer != null)
                {
                    bool wasReadable = importer.isReadable;
                    
                    if (!wasReadable)
                    {
                        importer.isReadable = true;
                        importer.SaveAndReimport();
                    }
                    
                    Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                    
                    return texture;
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Error loading texture {Path.GetFileName(texturePath)}: {ex.Message}");
            }
            
            return null;
        }
        
        /// <summary>
        /// Apply textures to materials after import
        /// Supports multi-part models with separate textures per material
        /// </summary>
        private void ApplyTexturesToMaterials(string assetPath, string modelAssetPath)
        {
            try
            {
                LogHelper.Verbose("Applying textures to materials...");

                // Detect render pipeline and select correct shader / property names
                var pipelineType = CurrentPipelineType;
                LogHelper.Verbose($"Using render pipeline branch: {pipelineType}");

                string shaderName, albedoProp, colorProp, metallicProp, normalProp, glossProp, metallicKeyword;
                switch (pipelineType)
                {
                    case RenderPipelineType.URP:
                        shaderName      = "Universal Render Pipeline/Lit";
                        albedoProp      = "_BaseMap";
                        colorProp       = "_BaseColor";
                        metallicProp    = "_MetallicGlossMap";
                        normalProp      = "_BumpMap";
                        glossProp       = "_Smoothness";
                        metallicKeyword = "_METALLICSPECGLOSSMAP";
                        break;
                    case RenderPipelineType.HDRP:
                        shaderName      = "HDRP/Lit";
                        albedoProp      = "_BaseColorMap";
                        colorProp       = "_BaseColor";
                        metallicProp    = "_MaskMap";
                        normalProp      = "_NormalMap";
                        glossProp       = null;
                        metallicKeyword = null;
                        break;
                    default: // Standard
                        shaderName      = "Standard";
                        albedoProp      = "_MainTex";
                        colorProp       = "_Color";
                        metallicProp    = "_MetallicGlossMap";
                        normalProp      = "_BumpMap";
                        glossProp       = "_Glossiness";
                        metallicKeyword = "_METALLICGLOSSMAP";
                        break;
                }
                
                // Find all materials in the import folder
                string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { assetPath });
                var materialPaths = materialGuids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(path => path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();
                var materialCandidateKeys = CollectMaterialContext(modelAssetPath, materialPaths, out List<string> orderedMaterialPaths);
                materialPaths = orderedMaterialPaths;
                
                if (materialPaths.Count == 0)
                {
                    LogHelper.Verbose("No materials found to configure");
                    return;
                }
                
                // Find and categorize all textures by part
                string[] textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { assetPath });
                var texturePaths = textureGuids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList();
                
                var baseColorTextures = new Dictionary<string, string>();
                var metallicSmoothnessTextures = new Dictionary<string, string>();
                var normalTextures = new Dictionary<string, string>();
                string modelKey = NormalizePartKey(Path.GetFileNameWithoutExtension(modelAssetPath));
                var modelPrefixKeys = new List<string>();
                AddUniqueKey(modelPrefixKeys, modelKey);
                AddUniqueKey(modelPrefixKeys, StripTrailingDuplicateNumber(modelKey));
                
                foreach (string texturePath in texturePaths)
                {
                    string fileNameWithoutExt = Path.GetFileNameWithoutExtension(texturePath).ToLower();
                    
                    // Base Color / Albedo
                    if (ContainsTextureToken(fileNameWithoutExt, "basecolor", "base_color", "albedo", "diffuse"))
                    {
                        AddTextureLookupKeys(baseColorTextures, fileNameWithoutExt, modelKey, modelPrefixKeys, texturePath);
                    }
                    
                    // Metallic Smoothness (combined texture we created)
                    if (ContainsTextureToken(fileNameWithoutExt, "smoothness"))
                    {
                        AddTextureLookupKeys(metallicSmoothnessTextures, fileNameWithoutExt, modelKey, modelPrefixKeys, texturePath);
                    }
                    
                    // Normal map
                    if (ContainsTextureToken(fileNameWithoutExt, "normal", "normalgl", "normaldx"))
                    {
                        AddTextureLookupKeys(normalTextures, fileNameWithoutExt, modelKey, modelPrefixKeys, texturePath);
                    }
                }
                
                // Apply textures to each material
                int configuredCount = 0;
                for (int materialIndex = 0; materialIndex < materialPaths.Count; materialIndex++)
                {
                    string materialPath = materialPaths[materialIndex];
                    Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    
                    if (material != null)
                    {
                        // Extract material name to match with texture parts
                        string rawMaterialName = Path.GetFileNameWithoutExtension(materialPath).ToLower();

                        var candidateKeys = new List<string>();
                        if (materialCandidateKeys.TryGetValue(materialPath, out List<string> rendererKeys))
                        {
                            foreach (string rendererKey in rendererKeys)
                                AddCandidateKey(candidateKeys, rendererKey);
                        }
                        AddCandidateKey(candidateKeys, rawMaterialName);
                        AddCandidateKey(candidateKeys, material.name);

                        // Set to the correct shader for the active render pipeline
                        if (material.shader.name != shaderName)
                        {
                            Shader targetShader = Shader.Find(shaderName);
                            if (targetShader != null)
                                material.shader = targetShader;
                            else
                                LogHelper.Warning($"Shader not found: {shaderName}. Material may render incorrectly.");
                        }

                        // Set albedo color to white (FFFFFF) to display textures correctly
                        material.SetColor(colorProp, Color.white);
                        
                        // Find matching textures for this material (only search types that exist)
                        string baseColorPath = baseColorTextures.Count > 0 ? FindMatchingTexture(candidateKeys, baseColorTextures) : null;
                        string metallicSmoothnessPath = metallicSmoothnessTextures.Count > 0 ? FindMatchingTexture(candidateKeys, metallicSmoothnessTextures) : null;
                        string normalPath = normalTextures.Count > 0 ? FindMatchingTexture(candidateKeys, normalTextures) : null;

                        // Skip material if no textures matched at all
                        if (string.IsNullOrEmpty(baseColorPath) && string.IsNullOrEmpty(metallicSmoothnessPath) && string.IsNullOrEmpty(normalPath))
                        {
                            LogHelper.Verbose($"Skipping material (no matching textures): {material.name}. Candidate keys: [{string.Join(",", candidateKeys)}]");
                            continue;
                        }
                        
                        LogHelper.Verbose($"Configuring material: {material.name} (base={Path.GetFileName(baseColorPath)}, metallicSmoothness={Path.GetFileName(metallicSmoothnessPath)}, normal={Path.GetFileName(normalPath)})");
                        
                        // Apply Base Color
                        if (!string.IsNullOrEmpty(baseColorPath))
                        {
                            Texture2D baseColorTex = AssetDatabase.LoadAssetAtPath<Texture2D>(baseColorPath);
                            if (baseColorTex != null)
                            {
                                material.SetTexture(albedoProp, baseColorTex);
                                LogHelper.Verbose($"Applied Base Color: {Path.GetFileName(baseColorPath)}");
                            }
                        }

                        // Apply Metallic/Smoothness
                        if (!string.IsNullOrEmpty(metallicSmoothnessPath))
                        {
                            Texture2D metallicTex = AssetDatabase.LoadAssetAtPath<Texture2D>(metallicSmoothnessPath);
                            if (metallicTex != null)
                            {
                                material.SetTexture(metallicProp, metallicTex);
                                material.SetFloat("_Metallic", 1.0f);
                                if (!string.IsNullOrEmpty(glossProp))
                                    material.SetFloat(glossProp, 1.0f);
                                if (!string.IsNullOrEmpty(metallicKeyword))
                                    material.EnableKeyword(metallicKeyword);
                                LogHelper.Verbose($"Applied Metallic/Smoothness: {Path.GetFileName(metallicSmoothnessPath)}");
                            }
                        }

                        // Apply Normal Map
                        if (!string.IsNullOrEmpty(normalPath))
                        {
                            Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                            if (normalTex != null)
                            {
                                material.SetTexture(normalProp, normalTex);
                                material.SetFloat("_BumpScale", 1.0f);
                                material.EnableKeyword("_NORMALMAP");
                                LogHelper.Verbose($"Applied Normal Map: {Path.GetFileName(normalPath)}");
                            }
                        }
                        
                        EditorUtility.SetDirty(material);
                        configuredCount++;
                    }
                }
                
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                if (configuredCount > 0)
                {
                    LogHelper.Verbose($"Configured {configuredCount} material(s) with PBR textures");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Material configuration error: {ex.Message}");
            }
        }

        /// <summary>
        /// Automatically fix normal map settings for imported textures
        /// </summary>
        private void FixNormalMaps(string assetPath)
        {
            try
            {
                LogHelper.Verbose("Checking for normal maps...");
                
                int fixedCount = 0;
                
                // Find all textures in the import folder
                string[] textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { assetPath });
                
                foreach (string guid in textureGuids)
                {
                    string texturePath = AssetDatabase.GUIDToAssetPath(guid);
                    string fileName = Path.GetFileName(texturePath).ToLower();
                    
                    // Check if filename contains "normal"
                    if (fileName.Contains("normal"))
                    {
                        TextureImporter texImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                        
                        if (texImporter != null && texImporter.textureType != TextureImporterType.NormalMap)
                        {
                            texImporter.textureType = TextureImporterType.NormalMap;
                            texImporter.SaveAndReimport();
                            fixedCount++;
                            LogHelper.Verbose($"Fixed normal map: {Path.GetFileName(texturePath)}");
                        }
                    }
                }
                
                if (fixedCount > 0)
                {
                    LogHelper.Verbose($"Fixed {fixedCount} normal map(s)");
                }
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Normal map fix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Add imported model to current scene
        /// </summary>
        private bool AddToScene(string assetPath, string modelFileName, string displayName)
        {
            try
            {
                LogHelper.Log("Adding to scene...");
                
                string modelAssetPath = Path.Combine(assetPath, modelFileName).Replace("\\", "/");
                
                // Load model
                GameObject modelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelAssetPath);
                if (modelPrefab == null)
                {
                    LogHelper.Log("Error: Could not load model prefab");
                    return false;
                }

                // Get active scene
                var scene = EditorSceneManager.GetActiveScene();
                
                // Instantiate in scene
                GameObject instance = PrefabUtility.InstantiatePrefab(modelPrefab, scene) as GameObject;
                if (instance == null)
                {
                    LogHelper.Log("Error: Could not instantiate prefab");
                    return false;
                }

                // Set position and name
                instance.transform.position = Vector3.zero;
                instance.name = displayName;
                
                // Select object
                Selection.activeGameObject = instance;
                
                // Mark scene dirty
                EditorSceneManager.MarkSceneDirty(scene);
                
                LogHelper.Log($"Model added to scene: {displayName}");
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.Error($"Scene add error: {ex.Message}");
                return false;
            }
        }

        // ---------------------------------------------------------------
        // Render pipeline detection
        // ---------------------------------------------------------------

        public enum RenderPipelineType { Standard, URP, HDRP }

        public static RenderPipelineType CurrentPipelineType { get; set; } = RenderPipelineType.Standard;

        public static void DetectAndSetRenderPipeline()
        {
            var rpa = GraphicsSettings.defaultRenderPipeline;
            if (rpa == null) 
            {
                CurrentPipelineType = RenderPipelineType.Standard;
                return;
            }
            string typeName = rpa.GetType().Name;
            if (typeName.Contains("Universal")) 
                CurrentPipelineType = RenderPipelineType.URP;
            else if (typeName.Contains("HD")) 
                CurrentPipelineType = RenderPipelineType.HDRP;
            else 
                CurrentPipelineType = RenderPipelineType.Standard;
        }

        /// <summary>
        /// Cleanup temporary directory with retry
        /// </summary>
        private void CleanupTempDirectory(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                return;

            for (int i = 0; i < MAX_CLEANUP_RETRIES; i++)
            {
                try
                {
                    Directory.Delete(path, true);
                    LogHelper.Verbose("Temporary files cleaned up");
                    return;
                }
                catch (IOException)
                {
                    if (i < MAX_CLEANUP_RETRIES - 1)
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Warning($"Cleanup warning: {ex.Message}");
                    return;
                }
            }
            
            LogHelper.Warning($"Warning: Could not delete temporary directory: {path}");
        }
    }
}
