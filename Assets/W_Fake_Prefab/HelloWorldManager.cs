using Unity.Netcode;
using UnityEngine;

namespace HelloWorld
{
    public class HelloWorldManager : MonoBehaviour
    {
        private NetworkManager m_NetworkManager;

        void Awake()
        {
            m_NetworkManager = GetComponent<NetworkManager>();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 300));
            if (!m_NetworkManager.IsClient && !m_NetworkManager.IsServer)
            {
                StartButtons();
            }
            else
            {
                StatusLabels();

                SubmitNewPosition();
            }

            GUILayout.EndArea();
        }

        public void StartButtons()
        {
            if (GUILayout.Button("Host")) m_NetworkManager.StartHost();
            if (GUILayout.Button("Client")) m_NetworkManager.StartClient();
            if (GUILayout.Button("Server")) m_NetworkManager.StartServer();
        }

        public void StatusLabels()
        {
            var mode = m_NetworkManager.IsHost ?
                "Host" : m_NetworkManager.IsServer ? "Server" : "Client";

            GUILayout.Label("Transport: " +
                m_NetworkManager.NetworkConfig.NetworkTransport.GetType().Name);
            GUILayout.Label("Mode: " + mode);
        }

        public void SubmitNewPosition()
        {
            if (m_NetworkManager.IsServer && !m_NetworkManager.IsClient)
            {
                foreach (ulong id in m_NetworkManager.ConnectedClientsIds)
                {
                    if (GUILayout.Button($"Move Player {id}"))
                    {
                        m_NetworkManager.SpawnManager.GetPlayerNetworkObject(id).GetComponent<HelloWorldPlayer>().Move();
                        Debug.Log($"Server is moving the player {id}");
                    }
                }
                // if (GUILayout.Button("Move Player 1"))
                // {
                //     ulong uid = m_NetworkManager.ConnectedClientsIds[0];
                //     m_NetworkManager.SpawnManager.GetPlayerNetworkObject(uid).GetComponent<HelloWorldPlayer>().Move();
                //     Debug.Log("Server is moving the player 1");
                // }
                // if (GUILayout.Button("Move Player 2"))
                // {
                //     ulong uid = m_NetworkManager.ConnectedClientsIds[1];
                //     m_NetworkManager.SpawnManager.GetPlayerNetworkObject(uid).GetComponent<HelloWorldPlayer>().Move();
                //     Debug.Log("Server is moving the player 2");
                // }
            }
            else
            {
                if (GUILayout.Button("Request Position Change"))
                {
                    var playerObject = m_NetworkManager.SpawnManager.GetLocalPlayerObject();
                    var player = playerObject.GetComponent<HelloWorldPlayer>();
                    player.Move();
                    Debug.Log("Client is moving the player");
                }
            }
        }
    }
}
