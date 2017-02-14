using System;
using UnityEngine;
using System.Collections.Generic;

public class SensoManager : MonoBehaviour 
{
    private bool m_rightEnabled;
    private bool m_leftEnabled;
    private bool m_bodyEnabled;

    public Transform[] Hands;
    private LinkedList<SensoHand> sensoHands;

    public Transform avatar;

    public SensoJoint Pelvis;
    public SensoJoint Spine;
    public SensoJoint Neck;
    public SensoJoint[] RightLeg;
    public SensoJoint[] LeftLeg;

    public SensoJoint[] RightArm;
    public SensoJoint[] LeftArm;

    private string GetSensoHost () {
        return searchCLIArgument("sensoHost", SensoHost);
    }
    private Int32 GetSensoPort () {
        var portStr = searchCLIArgument("sensoPort", SensoPort.ToString());
        Int32 port;
        if (!Int32.TryParse(portStr, out port)) {
            port = SensoPort;
        }
        return port;
    }
    public string SensoHost = "127.0.0.1"; //!< IP address of the Senso Server instane
    public Int32 SensoPort = 53450; //!< Port of the Senso Server instance

    private SensoNetworkThread sensoThread;

    public Transform orientationSource;
    private DateTime orientationNextSend;
    private double orientationSendEveryMS = 100.0f;

    void Start () {
        if (Hands != null && Hands.Length > 0) {
            sensoHands = new LinkedList<SensoHand>();
            for (int i = 0; i < Hands.Length; ++i) {
                Component[] components = Hands[i].GetComponents(typeof(SensoHand));
                for (int j = 0; j < components.Length; ++j) {
                    var hand = components[j] as SensoHand;
                    sensoHands.AddLast(hand);
                    if (!m_rightEnabled && hand.HandType == ESensoPositionType.RightHand) m_rightEnabled = true;
                    else if (!m_leftEnabled && hand.HandType == ESensoPositionType.LeftHand) m_leftEnabled = true;
                }
            }
        }
        
        // TODO: !!!
        m_bodyEnabled = true;
        if (Pelvis != null) Pelvis.OnStart();
        if (Spine != null) Spine.OnStart();
        if (Neck != null) Neck.OnStart();

        if (RightArm != null)
            for (int i = 0; i < RightArm.Length; ++i)
                RightArm[i].OnStart();
        if (LeftArm != null)
            for (int i = 0; i < LeftArm.Length; ++i)
                LeftArm[i].OnStart();
        if (RightLeg != null)
            for (int i = 0; i < RightLeg.Length; ++i)
                RightLeg[i].OnStart();
        if (LeftLeg != null)
            for (int i = 0; i < LeftLeg.Length; ++i)
                LeftLeg[i].OnStart();

        sensoThread = new SensoNetworkThread(GetSensoHost(), GetSensoPort());
        sensoThread.StartThread();
        //BroadcastMessage("SetSensoManager", this);
    }

    void OnDisable () {
        sensoThread.StopThread();
    }

    void FixedUpdate () {
        /*SensoHandData leftSample = null, rightSample = null;
        if (m_rightEnabled) {
            rightSample = sensoThread.GetSample(ESensoPositionType.RightHand);
        }
        if (m_leftEnabled) {
            leftSample = sensoThread.GetSample(ESensoPositionType.LeftHand);
        }
        if (sensoHands != null) {
            foreach (var hand in sensoHands) {
                if (hand.HandType == ESensoPositionType.RightHand) {
                    hand.SensoPoseChanged(rightSample);
                } else if (hand.HandType == ESensoPositionType.LeftHand) {
                    hand.SensoPoseChanged(leftSample);
                }
            }
        }
        
        // Gestures
        var gestures = sensoThread.GetGestures();
        if (gestures != null) {
            for (int i = 0; i < gestures.Length; ++i) {
                if (gestures[i].Type == ESensoGestureType.PinchStart || gestures[i].Type == ESensoGestureType.PinchEnd) {
                    fingerPinch(gestures[i].Hand, gestures[i].Fingers[0], gestures[i].Fingers[1], gestures[i].Type == ESensoGestureType.PinchEnd);
                }
            }
            
        }

        if (orientationSource != null && DateTime.Now >= orientationNextSend) {
            sensoThread.SendHMDOrientation(orientationSource.transform.localEulerAngles.y);
            orientationNextSend = DateTime.Now.AddMilliseconds(orientationSendEveryMS);
        }*/

        if (m_bodyEnabled)
        {
            var bodySample = sensoThread.GetBodySample();

            if (Pelvis != null)
            {
                Pelvis.ApplyQuaternion(bodySample.pelvisRotation);
            }
            if (Spine != null)
            {
                Spine.ApplyQuaternion(bodySample.spineRotation * Quaternion.Inverse(bodySample.pelvisRotation));
            }
            if (Neck != null)
            {
                Neck.ApplyQuaternion(bodySample.neckRotation * Quaternion.Inverse(bodySample.spineRotation));
            }
            if (RightLeg != null && RightLeg.Length == 3)
            {
                // Thigh
                RightLeg[0].ApplyQuaternion(bodySample.hipRotation[0], Pelvis);
                // Knee
                RightLeg[1].ApplyQuaternion(bodySample.kneeRotation[0] * Quaternion.Inverse(bodySample.hipRotation[0]));
                // Foot
                RightLeg[2].ApplyQuaternion(bodySample.footRotation[0] * Quaternion.Inverse(bodySample.kneeRotation[0]));
            }
            if (LeftLeg != null && LeftLeg.Length == 3)
            {
                // Thigh
                LeftLeg[0].ApplyQuaternion(bodySample.hipRotation[1], Pelvis);
                // Calf
                LeftLeg[1].ApplyQuaternion(bodySample.kneeRotation[1] * Quaternion.Inverse(bodySample.hipRotation[1]));
                // Foot
                LeftLeg[2].ApplyQuaternion(bodySample.footRotation[1] * Quaternion.Inverse(bodySample.kneeRotation[1]));
            }
            
            if (RightArm != null && RightArm.Length == 3)
            {
                // Clavicle
                RightArm[0].ApplyQuaternion(bodySample.clavicleRotation[0] * Quaternion.Inverse(bodySample.spineRotation));
                // Shoulder
                RightArm[1].ApplyQuaternion(bodySample.shoulderRotation[0] * Quaternion.Inverse(bodySample.clavicleRotation[0]));
                // Elbow
                RightArm[2].ApplyQuaternion(bodySample.elbowRotation[0] * Quaternion.Inverse(bodySample.shoulderRotation[0]));
            }
            if (LeftArm != null && LeftArm.Length == 3)
            {
                // Clavicle
                LeftArm[0].ApplyQuaternion(bodySample.clavicleRotation[1] * Quaternion.Inverse(bodySample.spineRotation));
                // Shoulder
                LeftArm[1].ApplyQuaternion(bodySample.shoulderRotation[1] * Quaternion.Inverse(bodySample.clavicleRotation[1]));
                // Elbow
                LeftArm[2].ApplyQuaternion(bodySample.elbowRotation[1] * Quaternion.Inverse(bodySample.shoulderRotation[1]));
            }

            avatar.localPosition = bodySample.position;
        }
    }

    ///
    /// @brief Send vibration command to the server
    ///
    public void SendVibro(ESensoPositionType hand, ESensoFingerType finger, ushort duration, byte strength)
    {
        sensoThread.VibrateFinger(hand, finger, duration, strength);
    }

    ///
    /// @brief Searches for the parameter in arguments list
    ///
    private static string searchCLIArgument (string param, string def = "") {
        if (Application.platform == RuntimePlatform.Android) {
            return def;
        }
        var args = System.Environment.GetCommandLineArgs();
        int i;
        string[] searchArgs = { param, "-" + param, "--" + param };

        for (i = 0; i < args.Length; ++i) {
            if (Array.Exists(searchArgs, elem => elem.Equals(args[i])) && args.Length > i + 1 ) {
                return args[i + 1];
            }
        }
        return def;
    }

    /// Events
    public void fingerPinch(ESensoPositionType handType, ESensoFingerType finger1Type, ESensoFingerType finger2Type, bool stop = false)
    {
        SensoHand aHand = null;
        foreach (var hand in sensoHands) 
            if (hand.HandType == handType) {
                aHand = hand;
                break;
            }

        if (aHand != null) {
            aHand.TriggerPinch(finger1Type, finger2Type, stop);
        }
    }
}
 