#pragma strict
var rotateSpeed = 30;
var texture : Texture;
var rotationCenter = Vector2.zero;

function Start() {
    // Create a new material with a shader
    // that rotates the texture. Texture rotation
    // is performed with a _Rotation matrix.
    var m : Material = new Material (
        "Shader \"Rotating Texture\" {" +
        "Properties { _MainTex (\"Base\", 2D) = \"white\" {} }" +
        "SubShader {" +
        "    Pass {" +
        "        Material { Diffuse (1,1,1,0) Ambient (1,1,1,0) }" +
        "        Lighting On" +
        "        SetTexture [_MainTex] {" +
        "            matrix [_Rotation]" +
        "            combine texture * primary double, texture" +
        "        }" +
        "    }" +
        "}" +
        "}"
    );
    m.mainTexture = texture;
    renderer.material = m;
}

function Update() {
    // Construct a rotation matrix and set it for the shader
    var rot = Quaternion.Euler (0, 0, Time.time * rotateSpeed);
    var m = Matrix4x4.TRS ( Vector3.zero, rot, Vector3(1,1,1) );
    var t = Matrix4x4.TRS (-rotationCenter, Quaternion.identity, Vector3(1,1,1));
    var t_inverse = Matrix4x4.TRS (rotationCenter, Quaternion.identity, Vector3(1,1,1));
    renderer.material.SetMatrix ("_Rotation", t_inverse*m*t);
}