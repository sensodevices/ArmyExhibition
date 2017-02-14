using UnityEngine;
using System;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using SimpleJSON;

///
/// @brief States of the network thread
public enum SensoNetworkState {
	SENSO_DISCONNECTED, SENSO_CONNECTING, SENSO_CONNECTED, SENSO_FAILED_TO_CONNECT, SENSO_ERROR, SENSO_FINISHED, SENSO_STATE_NUM
};

///
/// @brief Class that connects to the Senso Server and provides pose samples
///
public class SensoNetworkThread
{
    private SensoNetworkState m_state = SensoNetworkState.SENSO_DISCONNECTED;
    public SensoNetworkState State {
    get { return m_state; }
        private set { m_state = value; }
    }
    private Int32 m_port; //!< Port of the Senso server
    private IPAddress m_ip; //!< IPAddress of the Senso server
    private Thread m_thread = null; //!< Thread where socket networking happend
    private volatile bool m_isRunning; //!< flag if thread should continue running
    private int RECV_BUFFER_SIZE = 4096; //!< Size of the buffer for read operations
    private int SEND_BUFFER_SIZE = 4096; //!< Size of the buffer to send
    private static int SAMPLES_COUNT = 50;

    private Dictionary<int, SensoHandData[]> m_samples;
    private int[] m_nextSampleInd;

    private SensoBodyData[] m_bodySamples = new SensoBodyData[SAMPLES_COUNT];
    private int m_nextBodySampleInd;

    private LinkedList<SensoHandGesture> gestures;
    private System.Object gesturesLock = new System.Object();
    public int GesturesCount { get; private set; }

    ///
    /// @brief Structure to store vibration packet
    private struct VibroPacket
    {
        private ushort m_duration;
        private byte m_strength;
        public bool changed { get; private set; }

        public void Change(ushort duration, byte strength) {
            m_duration = duration;
            m_strength = strength;
            changed = true;
        }

        public int GetJSON(ESensoPositionType hand, ESensoFingerType finger, ref Byte[] buf, ref int bufOffset) {
            var str = String.Format("{{\"dst\":\"{0}\",\"type\":\"vibration\",\"data\":{{\"type\":{1},\"dur\":{2},\"str\":{3}}}}}\n", (hand == ESensoPositionType.RightHand ? "rh" : "lh"), (int)finger, m_duration, m_strength);
            changed = false;
            return Encoding.ASCII.GetBytes(str, 0, str.Length, buf, bufOffset);
        }
    };

    private struct OrientationPacket
    {
        public bool changed { get; private set; }
        private float yaw;
        public void Change (float newYaw) {
            yaw = newYaw;
            changed = true;
        }
        public int GetJSON(ref Byte[] buf, ref int bufOffset) {
            var str = String.Format("{{\"type\":\"orientation\",\"data\":{{\"type\":\"hmd\",\"yaw\":{0}}}}}\n", yaw * Mathf.Deg2Rad);
            changed = false;
            return Encoding.ASCII.GetBytes(str, 0, str.Length, buf, bufOffset);
        }
    };

    private VibroPacket[] vibroPackets; //!< Vibration packets to send to server
    private System.Object vibroLock = new System.Object(); //!< Lock to use when working with vibroPackets

    private OrientationPacket orientationPacket;
    private System.Object orientationLock = new System.Object();

    ///
    /// @brief Default constructor
    ///
    public SensoNetworkThread (string host, Int32 port)
    {
        m_port = port;    
        if (!IPAddress.TryParse(host, out m_ip)) {
            m_ip = null;
        }
        m_samples = new Dictionary<int, SensoHandData[]>();
        m_nextSampleInd = new int[(int)ESensoPositionType.PositionsCount];
        for (int i = 0; i < (int)ESensoPositionType.PositionsCount; ++i) {
            m_samples[i] = new SensoHandData[SAMPLES_COUNT];
            for (int j = 0; j < SAMPLES_COUNT; ++j) {
                m_samples[i][j] = new SensoHandData();
            }
            m_nextSampleInd[i] = 0;
        }

        for (int j = 0; j < SAMPLES_COUNT; ++j)
            m_bodySamples[j] = new SensoBodyData();

        vibroPackets = new VibroPacket[10]; // 5 for each hand. 1-5 = right hand, 6-10 = left hand
        for (int i = 0; i < vibroPackets.Length; ++i) {
            vibroPackets[i] = new VibroPacket();
        }
        gestures = new LinkedList<SensoHandGesture>();
        GesturesCount = 0;
    }

    ///
    /// @brief starts the thread that reads from socket
    ///
    public void StartThread () {
        if (m_thread == null) {
            m_isRunning = true;
            m_thread = new Thread(Run);
            m_thread.Start();
        }
    }

    ///
    /// @brief Stops the thread that reads from socket
    ///
    public void StopThread () {
        if (m_thread != null) {
            m_isRunning = false;
            m_thread.Join();
        }
    }

    ///
    /// @brief Returns current sample of the specified hand
    ///
    public SensoHandData GetSample (ESensoPositionType handType) {
        int ind = (int)handType;
        int sampleInd = 0;
        sampleInd = m_nextSampleInd[ind] - 1;
        if (sampleInd < 0) sampleInd += SAMPLES_COUNT;
        return new SensoHandData(m_samples[ind][sampleInd]);
    }

    ///
    /// @brief Returns current sample of the specified hand
    ///
    public SensoBodyData GetBodySample()
    {
        int sampleInd = 0;
        sampleInd = m_nextBodySampleInd - 1;
        if (sampleInd < 0) sampleInd += SAMPLES_COUNT;
        return new SensoBodyData(m_bodySamples[sampleInd]);
    }

    private void Run () {
        if (m_ip == null) return;
        int inBufferOffset = 0;
        int outBufferOffset = 0;
        Byte[] inBuffer = new Byte[RECV_BUFFER_SIZE];
        Byte[] outBuffer = new Byte[SEND_BUFFER_SIZE];

        while (m_isRunning) {
          var sock = new TcpClient(AddressFamily.InterNetwork);
          var conRes = sock.BeginConnect(m_ip, m_port, null, null);
          var connected = conRes.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(0.5));
          if (connected) {
            inBufferOffset = 0;
            NetworkStream stream = null;
            try {
              stream = sock.GetStream();
              stream.ReadTimeout = 500;
            } catch (Exception ex) {
              Debug.LogError("Get stream exception: " + ex.Message);
            }
            int readSz = 0;
            if (stream == null) continue;

            while (sock.Connected && m_isRunning) {
              try {
                readSz = stream.Read(inBuffer, inBufferOffset, RECV_BUFFER_SIZE - inBufferOffset);
              } catch (Exception ex) {
                Debug.LogError("Read exception: " + ex.Message);
              }
              if (readSz == 0) break;
              inBufferOffset += readSz;
              parsePacket(ref inBuffer, ref inBufferOffset);

              outBufferOffset = 0;
              netSendVibro(ref outBuffer, ref outBufferOffset);
              netSendOrientation(ref outBuffer, ref outBufferOffset);
              if (outBufferOffset > 0) {
                // Debug.Log(Encoding.ASCII.GetString(outBuffer, 0, outBufferOffset));
                try {
                  stream.Write(outBuffer, 0, outBufferOffset);
                } catch (Exception ex) {
                  Debug.LogError("Write exception: " + ex.Message);
                }
              }
            }
            sock.Close();
          } else {
            Thread.Sleep(1000);
          }
        }
    }

  private void parsePacket (ref Byte[] buf, ref int size) {
    bool hasReceivedPacket;
    //Debug.Log(Encoding.ASCII.GetString(buf, 0, size));
            
    do {
      hasReceivedPacket = false;
      for (int i = 0; i < size; ++i) {
        if (buf[i] == 10) {
          hasReceivedPacket = true;
          processJsonStr(Encoding.ASCII.GetString(buf, 0, i));
          if (i + 1 < size) {
            Buffer.BlockCopy(buf, i + 1, buf, 0, size - i - 1);
            size = size - i - 1;
          } else {
            size = 0;
          }
          break;
        }
      }
    } while (hasReceivedPacket);
  }

    ///
    /// @brief Parses JSON packet received from server
    ///
    private bool processJsonStr (string jsonPacket)
    {
        JSONNode parsedData = null;
        try
        {
            parsedData = JSON.Parse(jsonPacket);
        }
        catch (Exception ex)
        {
            Debug.LogError("packet parse error: " + ex.Message);
        }
        if (parsedData != null)
        {
            if (parsedData["type"].Value.Equals("position"))
            {
                var dataNode = parsedData["data"];
                int ind = 0;
                if (dataNode["type"].Value.Equals("rh"))
                {
                    ind = 1;
                }
                else if (dataNode["type"].Value.Equals("lh"))
                {
                    ind = 2;
                }
                else if (dataNode["type"].Value.Equals("body"))
                {
                    try
                    {
                        m_bodySamples[m_nextBodySampleInd].parseJSONNode(dataNode);
                        ++m_nextBodySampleInd;
                        if (m_nextBodySampleInd >= SAMPLES_COUNT) m_nextBodySampleInd -= SAMPLES_COUNT;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(ex.Message);
                    }
                    return true;
                }

                var sampleInd = m_nextSampleInd[ind];
                try
                {
                    m_samples[ind][sampleInd].parseJSONNode(dataNode);
                }
                catch (Exception ex)
                {
                    Debug.LogError(ex.Message);
                }
                {
                    ++m_nextSampleInd[ind];
                    if (m_nextSampleInd[ind] >= SAMPLES_COUNT) m_nextSampleInd[ind] -= SAMPLES_COUNT;
                }
            }
            else if (parsedData["type"].Value.Equals("gesture"))
            {
                SensoHandGesture aGesture = new SensoHandGesture(parsedData["data"]);
                lock (gesturesLock)
                {
                    gestures.AddLast(aGesture);
                    ++GesturesCount;
                }
            }
            else
            {
                Debug.Log("Received: " + jsonPacket);
            }
        }
        return true;
    }

    ///
    /// @brief Send vibrating command to the server
    ///
    public void VibrateFinger (ESensoPositionType handType, ESensoFingerType fingerType, ushort duration, byte strength) {
        int handMult = handType == ESensoPositionType.RightHand ? 0 : 1;
        int ind = handMult * 5 + (int)fingerType;
        lock(vibroLock)
            vibroPackets[ind].Change(duration, strength);
    }

    private void netSendVibro (ref Byte[] buf, ref int offset) {
        ESensoPositionType hand = ESensoPositionType.Unknown;
        ESensoFingerType finger = ESensoFingerType.Thumb;
        lock(vibroLock) {
            for (int i = 0; i < 10; ++i) {
            if (i % 5 == 0) ++hand;
            if (vibroPackets[i].changed) {
                offset += vibroPackets[i].GetJSON(hand, finger, ref buf, ref offset);
            }
            if (i % 5 == 4) finger = ESensoFingerType.Thumb;
            else ++finger;
            }
        }
    }

    private void netSendOrientation (ref Byte[] buf, ref int offset) {
        if (orientationPacket.changed) {
            lock(orientationLock)
            offset += orientationPacket.GetJSON(ref buf, ref offset);
        }
    }

    ///
    /// @brief Receive gestures from server
    ///
    public SensoHandGesture[] GetGestures () {
        if (GesturesCount > 0) {
            SensoHandGesture[] receivedGestures = new SensoHandGesture[GesturesCount];
            lock (gesturesLock) {
            var enumerator = gestures.GetEnumerator();
            for (int i = 0; enumerator.MoveNext(); ++i) {
                receivedGestures[i] = enumerator.Current;
            }
            gestures.Clear();
            GesturesCount = 0;
            }
            return receivedGestures;
        } else {
            return null;
        }
    }

    ///
    /// @brief Sends HMD orientation to Senso Server
    ///
    public void SendHMDOrientation (float yaw) {
        lock(orientationLock)
            orientationPacket.Change(yaw);
    }
  /*public Transform VRCamera;
  public bool cameraSender = true;

  private DateTime lastVRCameraSent = DateTime.Now;

  private Quaternion cameraRotation;
  private Quaternion fromCamRotation;
  private float cameraRotDT;


  void Update()
  {
    if (!cameraSender) {
      if (cameraRotDT == 0.0f) {
        fromCamRotation = VRCamera.localRotation;
      }
      if (cameraRotDT < 1.0f) {
        cameraRotDT += (Time.deltaTime * 10.0f);
        VRCamera.localRotation = Quaternion.Lerp(fromCamRotation, cameraRotation, cameraRotDT);
      } else {
        if (VRCamera.localRotation != cameraRotation) {
          VRCamera.localRotation = cameraRotation;
        }
      }
    }
  }

      var now = DateTime.Now;
      var dt = now - lastVRCameraSent;
      var dataDT = now - lastSensoData;
      if (cameraSender) {
        if (!tcpState.is_sending && dt.TotalMilliseconds >= 100) {
          var rotationArray = new float[] { 
            VRCamera.localRotation.w, VRCamera.localRotation.x, VRCamera.localRotation.y, VRCamera.localRotation.z,
            VRCamera.localPosition.x, VRCamera.localPosition.z, VRCamera.localPosition.y 
          };
          int len = rotationArray.Length * 4;
          byte[] msg = new byte[2 + len];
          msg[0] = 0x02;
          msg[1] = (byte)len;
          Buffer.BlockCopy(rotationArray, 0, msg, 2, len);

          tcpState.is_sending = true;
          try {
            tcpState.stream.BeginWrite(msg, 0, 2 + len, m_SendCb, tcpState);
          } catch (Exception err) {
            HandleDisconnect(3);
          } finally {
            lastVRCameraSent = now;
          }
        }
      }
    } else if (!tcpState.waitConnect) {
        var diff = DateTime.Now - tcpState.lastConnectRetry;
        if (diff.TotalSeconds > 5) {
          do_connect();
        }
    } */
}
