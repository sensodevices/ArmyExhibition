using UnityEngine;
using SimpleJSON;

public class SensoBodyData
{
    public Quaternion pelvisRotation;
    public Quaternion spineRotation;
    public Quaternion neckRotation;

    public Vector3 position;
    public Quaternion[] clavicleRotation = new Quaternion[2];
    public Quaternion[] shoulderRotation = new Quaternion[2];
    public Quaternion[] elbowRotation = new Quaternion[2];
    public Quaternion[] hipRotation = new Quaternion[2];
    public Quaternion[] kneeRotation = new Quaternion[2];
    public Quaternion[] footRotation = new Quaternion[2];

    ///
    /// @brief Default constructor
    ///
    public SensoBodyData()
    {
        pelvisRotation = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
        spineRotation = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);
        neckRotation = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        for (int i = 0; i < 2; ++i)
            clavicleRotation[i] = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        for (int i = 0; i < 2; ++i)
            shoulderRotation[i] = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        for (int i = 0; i < 2; ++i)
            elbowRotation[i] = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        for (int i = 0; i < 2; ++i)
            hipRotation[i] = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        for (int i = 0; i < 2; ++i)
            kneeRotation[i] = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        for (int i = 0; i < 2; ++i)
            footRotation[i] = new Quaternion(0.0f, 0.0f, 0.0f, 1.0f);

        position = new Vector3(0.0f, 0.0f, 0.0f);
    }

    ///
	/// @brief Copy constructor
	///
	public SensoBodyData(SensoBodyData old)
    {
        pelvisRotation = old.pelvisRotation;
        spineRotation = old.spineRotation;
        neckRotation = old.neckRotation;

        for (int i = 0; i < 2; ++i)
            clavicleRotation[i] = old.clavicleRotation[i];

        for (int i = 0; i < 2; ++i)
            shoulderRotation[i] = old.shoulderRotation[i];

        for (int i = 0; i < 2; ++i)
            elbowRotation[i] = old.elbowRotation[i];

        for (int i = 0; i < 2; ++i)
            hipRotation[i] = old.hipRotation[i];

        for (int i = 0; i < 2; ++i)
            kneeRotation[i] = old.kneeRotation[i];

        for (int i = 0; i < 2; ++i)
            footRotation[i] = old.footRotation[i];

        position = old.position;
    }

    ///
	/// @brief Parses JSON node into internal properties
	///
	public void parseJSONNode(JSONNode data)
    {
        JSONArray anArr;

        // Pelvis parsing
        anArr = data["pelvis"]["quat"].AsArray;
        arrToQuat(ref anArr, ref pelvisRotation);

        // Spine parsing
        anArr = data["spine"]["quat"].AsArray;
        arrToQuat(ref anArr, ref spineRotation);

        // Neck parsing
        anArr = data["neck"]["quat"].AsArray;
        arrToQuat(ref anArr, ref neckRotation);

        JSONNode aNode = data["clavicle"];
        parseLeftRightQuat(ref aNode, ref clavicleRotation);
        
        aNode = data["shoulder"];
        parseLeftRightQuat(ref aNode, ref shoulderRotation);

        aNode = data["elbow"];
        parseLeftRightQuat(ref aNode, ref elbowRotation);

        aNode = data["hip"];
        parseLeftRightQuat(ref aNode, ref hipRotation);

        aNode = data["knee"];
        parseLeftRightQuat(ref aNode, ref kneeRotation);

        aNode = data["foot"];
        parseLeftRightQuat(ref aNode, ref footRotation);

        aNode = data["position"];
        if (aNode != null)
        {
            anArr = aNode.AsArray;
            SensoHandData.arrToVec3(ref anArr, ref position); 
        }
    }

    static private void arrToQuat(ref JSONArray arr, ref Quaternion quat)
    {
        quat.w = arr[0].AsFloat;
        quat.x = arr[1].AsFloat;
        quat.y = arr[3].AsFloat;
        quat.z = arr[2].AsFloat;
    }

    static private void parseLeftRightQuat(ref JSONNode node, ref Quaternion[] quatArr)
    {
        var anArr = node["right"]["quat"].AsArray;
        arrToQuat(ref anArr, ref quatArr[0]);

        anArr = node["left"]["quat"].AsArray;
        arrToQuat(ref anArr, ref quatArr[1]);
    }
}
