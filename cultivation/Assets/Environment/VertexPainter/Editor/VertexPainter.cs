using UnityEngine;
using UnityEditor;
using System.Collections;

public static class VertexShaders {
	static private Material  vertexMaterial;
	static public Material ShowVertexColor  {
		get {
			if (vertexMaterial != null) return vertexMaterial;
			string shader = 
				"Shader \"Vertex Data\" {" +
			    "Properties {	_MainTex (\"Texture\", 2D) = \"white\" {} }" +
				"Category {" +
				"	Tags { \"Queue\"=\"Geometry\"}" +
				"	Lighting Off" +
				"	BindChannels {" +
				"	Bind \"Color\", color" +
				"	Bind \"Vertex\", vertex" +
				"	Bind \"TexCoord\", texcoord" +
				"}" +
				"SubShader {" +
				"	Pass {" +
				"	SetTexture [_MainTex] {" +
				"		combine primary + primary" +
				"	}" +
				"	}" +
				"}" +
				"SubShader {" +
				"	Pass {" +
				"	SetTexture [_MainTex] {" +
				"		constantColor (1,1,1,1)" +
				"		combine texture lerp(texture) constant" +
				"	}" +
				"}" +
				"}"+
				"}}";
			vertexMaterial = new Material(shader);
			return vertexMaterial;
		}
		set{}
	}
}

public class VertexPainter : EditorWindow {
  	
	enum Mode { 
		None,
		Painting
	}
	static Mode 					currentMode = Mode.None;
	static GameObject 				currentSelection;
	static Mesh 					currentSelectionMesh;
	static MeshFilter				currentSelectionMeshFilter;
	static Color 					currentColor;
	static float 					radius;
	static float 					blendFactor;
	static SceneView.OnSceneFunc 	onSceneGUIFunc;
	static VertexPainter 			window;
	static bool						RenderVertexColors;
	static Material 				oldMaterial;
	static int 						VertexPainterHash;
	
	GUIStyle boxBackground;
	
	[MenuItem ("Window/VertexPaint")]
    static void Init () {
        window = (VertexPainter)EditorWindow.GetWindow (typeof (VertexPainter));
		
		onSceneGUIFunc = new SceneView.OnSceneFunc(OnSceneGUI);
		SceneView.onSceneGUIDelegate += onSceneGUIFunc;
		
		VertexPainterHash = window.GetHashCode();
		currentSelection = Selection.activeGameObject;
		if (currentSelection!= null) {
			currentSelectionMeshFilter = currentSelection.GetComponent<MeshFilter>();
			
			if (currentSelectionMeshFilter!= null) 
				currentSelectionMesh = currentSelectionMeshFilter.sharedMesh;
			else
				Debug.Log("meshfilter null");
		}
    }
	
	static void PaintVertexColors() {
		
		Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
		RaycastHit hit;
		if (Physics.Raycast(r,out hit,float.MaxValue)) {
			Vector3[] vertices = currentSelectionMesh.vertices;
   			Color[] colrs  = currentSelectionMesh.colors;
			
			Undo.RegisterUndo(currentSelectionMesh, "Vertex paint");
			
			Vector3 pos = currentSelection.transform.InverseTransformPoint(hit.point);
			for (int i=0;i<vertices.Length;i++)
			{
				float sqrMagnitude = (vertices[i] - pos).magnitude;
				if (sqrMagnitude > radius)
					continue;
				Color newColor = Color.Lerp (colrs[i], currentColor, blendFactor);
				colrs[i] = newColor;
			}
			currentSelectionMesh.colors = colrs;

		
		} 
	}
	
	static void DrawHandle() {
		Ray r = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
		RaycastHit hit;
		if (Physics.Raycast(r,out hit,float.MaxValue)) {
			Handles.DrawWireDisc(hit.point,hit.normal,radius);		
			
		} 
	}
	
	static void SetMaterial() {
		if (RenderVertexColors) {
			if (oldMaterial == null) { 
				oldMaterial = currentSelection.GetComponent<Renderer>().sharedMaterial;
				currentSelection.GetComponent<Renderer>().sharedMaterial = VertexShaders.ShowVertexColor;
			}
		} else {
			if (oldMaterial != null) {
				currentSelection.GetComponent<Renderer>().sharedMaterial = oldMaterial;
				oldMaterial = null;
			}
			
		}
	}
	
	public static void OnSceneGUI(SceneView sceneview)
	{
		if ((currentSelection== null) || (currentSelectionMesh == null) || (currentSelectionMeshFilter == null) ) 
			return;
		
		SetMaterial();
		
		int ctrlID = GUIUtility.GetControlID (VertexPainterHash , FocusType.Passive);
		Event current = Event.current;
		
		switch(current.type){
		case EventType.KeyUp :
			if ((current.keyCode == KeyCode.Q) && (current.control))
				RenderVertexColors = !RenderVertexColors;
			break;
			
		case EventType.MouseUp:
			switch (currentMode) {
				case Mode.Painting:

				break;
			}
			break;
		case EventType.MouseDown:
			
			switch (currentMode) {
				case Mode.Painting:
					current.Use();
				break;
			}
			break;
		case EventType.MouseDrag:
			switch (currentMode) {
				case Mode.None:
					
				break;
				case Mode.Painting:
					EditorUtility.SetDirty(currentSelectionMesh);
					PaintVertexColors();
				break;
			}
			DrawHandle();
			HandleUtility.Repaint();
			break;
		case EventType.MouseMove:
			HandleUtility.Repaint();
			break;
		case EventType.Repaint:
			DrawHandle();
			break;
		case EventType.Layout:
			HandleUtility.AddDefaultControl(ctrlID);
			break; 
		}
	}
	
	void GenerateStyles( ) {
		if (boxBackground == null) {
			boxBackground = new GUIStyle();
			
			boxBackground.padding.top+=5;
			boxBackground.padding.bottom+=5;
		}
	}
	
	void Save() {
		if ((currentSelection== null) || (currentSelectionMesh == null) || (currentSelectionMeshFilter == null) ) 
			return;
		
		AssetDatabase.Refresh();
		
		int id = currentSelection.GetInstanceID();
		string p = AssetDatabase.GetAssetPath(currentSelectionMesh);
		
		string toDelete = "";
		if ((p.Contains(".assets")) && (!p.Contains(id.ToString()))) {
			toDelete = p;
		}
		
		Mesh newMesh = new Mesh();
		newMesh.vertices = currentSelectionMesh.vertices;
		newMesh.triangles = currentSelectionMesh.triangles;
		newMesh.colors = currentSelectionMesh.colors;
		newMesh.tangents = currentSelectionMesh.tangents;
		newMesh.normals = currentSelectionMesh.normals;
		newMesh.uv = currentSelectionMesh.uv;
		newMesh.uv2 = currentSelectionMesh.uv2;
		newMesh.uv2 = currentSelectionMesh.uv2;
		
		newMesh.RecalculateBounds();
		newMesh.RecalculateNormals();
		
		if (p != "") p = System.IO.Path.GetDirectoryName(p);
		else p = "Assets";
		
		string newPath = p + "/"  + currentSelection.name  + "_" + id +".assets";
		
		AssetDatabase.CreateAsset(newMesh, newPath);
		
		currentSelectionMeshFilter.sharedMesh = newMesh;
		MeshCollider mC = currentSelection.GetComponent<MeshCollider>();
		if (mC != null)
			mC.sharedMesh = newMesh;
		
		currentSelectionMesh = newMesh;
		
		if (toDelete!= "") {
			AssetDatabase.DeleteAsset(toDelete);
		}
		
		EditorUtility.SetSelectedWireframeHidden(currentSelection.GetComponent<Renderer>(), false);
		EditorUtility.SetDirty(currentSelection);
		
	
		AssetDatabase.Refresh();
	
	}
	
	void OnDestroy()
	{
		Save();
		
		if (oldMaterial != null) {
			if (currentSelection != null)
				if (currentSelection.GetComponent<Renderer>() != null)
					currentSelection.GetComponent<Renderer>().sharedMaterial = oldMaterial;
		}
		oldMaterial = null;
		currentMode = Mode.None;
		SceneView.onSceneGUIDelegate -= onSceneGUIFunc;
		window = null;
	}
    
    void OnGUI () {
		GenerateStyles();
        EditorGUILayout.LabelField ("Vertexpainter by Pixelstudio V1.0", EditorStyles.boldLabel);
		EditorGUILayout.LabelField ("INFO :On reimport all changes are gone.");
		EditorGUILayout.LabelField ("INFO :Press stop button to apply colors!!");
		EditorGUILayout.Space();
		
		GUI.enabled = (currentSelectionMesh != null);
		
		EditorGUILayout.ObjectField("Current selection ", currentSelection, typeof(GameObject), true);
		
		EditorGUILayout.BeginVertical(boxBackground);
		currentColor = EditorGUILayout.ColorField("Color to paint",currentColor);
		Color old = GUI.color;
		EditorGUILayout.BeginHorizontal();
		GUI.color = Color.red;
		if (GUILayout.Button("Red")) currentColor = new Color(1,0,0,0);
		GUI.color = Color.green;
		if (GUILayout.Button("Green")) currentColor = new Color(0,1,0,0);;
		GUI.color = Color.blue;
		if (GUILayout.Button("Blue")) currentColor =new Color(0,0,1,0);;
		GUI.color = old;
		if (GUILayout.Button("Alpha")) currentColor =new Color(0,0,0,1);;
	
		EditorGUILayout.EndHorizontal();
		EditorGUILayout.EndVertical();
		
		radius = Mathf.Clamp(EditorGUILayout.FloatField("Radius", radius), 0.1f, 100f);
		blendFactor = Mathf.Clamp(EditorGUILayout.FloatField("Blend", blendFactor), 0.01f, 1f);
		
		GUIContent showVertexLbl = new GUIContent ("Show vertex colors", "Shortcut CTRL-Q");
		RenderVertexColors = EditorGUILayout.Toggle(showVertexLbl ,RenderVertexColors);
		
		if (currentSelectionMesh != null) {
			if (currentSelectionMesh.colors.Length != currentSelectionMesh.vertices.Length) {
				if (GUILayout.Button("Generate vertex array")) {
					currentSelectionMesh.colors	 = new Color[currentSelectionMesh.vertices.Length];
					EditorUtility.SetDirty(currentSelection);
							EditorUtility.SetDirty(currentSelectionMesh);
							AssetDatabase.SaveAssets();
							AssetDatabase.SaveAssets();
				}
			} else {
				
				if (GUILayout.Button("Set all to choosen color")) {
					ResetVertexColors();
				}
				switch (currentMode) {
					case Mode.None:
						if (GUILayout.Button("Paint"))  {
							EditorUtility.SetSelectedWireframeHidden(currentSelection.GetComponent<Renderer>(), true);
							currentMode = Mode.Painting;
						}
					break;
					case Mode.Painting:
						if (GUILayout.Button("Stop")) {
							Save();
							currentMode = Mode.None;
						}
					break;
				}
			}
		}
		
    }
	
	void OnSelectionChange() {
		currentMode = Mode.None;
		
		currentSelection = Selection.activeGameObject;
		
		if (currentSelection!= null) {
			currentSelectionMeshFilter = currentSelection.GetComponent<MeshFilter>();
			if (currentSelectionMeshFilter!= null) 
				currentSelectionMesh = currentSelectionMeshFilter.sharedMesh;
			
		}
		Repaint();
	}
	
	void ResetVertexColors() {
		Color[] colrs  = currentSelectionMesh.colors;
		for (int i = 0; i < colrs.Length; i++) {
			colrs[i] = currentColor;		
		}
		currentSelectionMesh.colors = colrs;
	}
	
}

