using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text;
using System.Net.Sockets;
using System.Net;

public class NetworkMan : MonoBehaviour
{
    public UdpClient udp;
    // Start is called before the first frame update
    void Start()
    {
        udp = new UdpClient();

        udp.Connect("3.130.200.122", 12345);
        //udp.Connect("localhost", 12345);

        Byte[] sendBytes = Encoding.ASCII.GetBytes("connect"); // Send connect message to server
      
        udp.Send(sendBytes, sendBytes.Length);

        udp.BeginReceive(new AsyncCallback(OnReceived), udp);

        InvokeRepeating("HeartBeat", 1, 0.03f); // Every 0.03 seconds run heartbeat
    }

    void OnDestroy(){
        udp.Dispose();
    }


    public enum commands{
        NEW_CLIENT, // == 0
        UPDATE, // == 1
        DROPPED_CLIENT
    };
    
    [Serializable]
    public class Message{
        public commands cmd;
    }
    
    [Serializable]
    public class Player{
        [Serializable]
        public struct receivedPosition{
            public float X;
            public float Y;
            public float Z;
        }

        public string id;
        public receivedPosition position;
    }

    [Serializable]
    public class NewPlayer{
        public Player player;
    }

    [Serializable]
    public class GameState{
        public Player[] players;
    }

    public class DroppedPlayers{
        public Player[] players;
    }

    public Message latestMessage;
    public GameState latestGameState;
    public NewPlayer newPlayer;
    public DroppedPlayers latestDroppedPlayers;

    void OnReceived(IAsyncResult result){
        // this is what had been passed into BeginReceive as the second parameter:
        UdpClient socket = result.AsyncState as UdpClient;
        
        // points towards whoever had sent the message:
        IPEndPoint source = new IPEndPoint(0, 0);

        // get the actual message and fill out the source:
        byte[] message = socket.EndReceive(result, ref source);
        
        // do what you'd like with `message` here:
        string returnData = Encoding.ASCII.GetString(message); // Json string; m (json dump) that was sent by server
        Debug.Log("Got this: " + returnData);
        
        latestMessage = JsonUtility.FromJson<Message>(returnData);
        try{
            switch(latestMessage.cmd){
                case commands.NEW_CLIENT:
                    newPlayer = JsonUtility.FromJson<NewPlayer>(returnData);
                    JsonUtility.FromJson<NewPlayer>(returnData);
                    break;
                case commands.UPDATE:
                    latestGameState = JsonUtility.FromJson<GameState>(returnData);
                    break;
                case commands.DROPPED_CLIENT:
                    latestDroppedPlayers = JsonUtility.FromJson<DroppedPlayers>(returnData);
                    break;
                default:
                    Debug.Log("Error");
                    break;
            }
        }
        catch (Exception e){
            Debug.Log(e.ToString());
        }
        
        // schedule the next receive operation once reading is done:
        socket.BeginReceive(new AsyncCallback(OnReceived), socket);
    }

    private Dictionary<string, GameObject> playerCharacterList = new Dictionary<string, GameObject>();
    public GameObject playerCharacterPrefab;
    GameObject peerPlayerCharacter;
    Player ownPlayerCharacter = null;

    void SpawnPlayers()
    {
        // A new client does not have any characters spawned
        if (playerCharacterList.Count <= 0)
        {
            // The character that was just added is the new client's character
            ownPlayerCharacter = newPlayer.player;

            foreach (Player p in latestGameState.players)
            {
                Vector3 spawnPosition = new Vector3(p.position.X, p.position.Y, p.position.Z);
                peerPlayerCharacter = Instantiate(playerCharacterPrefab, spawnPosition, transform.rotation);
                peerPlayerCharacter.GetComponent<PlayerNetworkID>().id = p.id;
                playerCharacterList.Add(p.id, peerPlayerCharacter);
            }

            if (playerCharacterList.ContainsKey(ownPlayerCharacter.id))
            {
                playerCharacterList[ownPlayerCharacter.id].AddComponent<CharacterMovement>();
            }
        }
        // Existing client with mismatched list of players and characters
        else if (playerCharacterList.Count < latestGameState.players.Length)
        {
            Vector3 spawnPosition = new Vector3(UnityEngine.Random.Range(0, 5), UnityEngine.Random.Range(0, 5));
            peerPlayerCharacter = Instantiate(playerCharacterPrefab, spawnPosition, transform.rotation);
            peerPlayerCharacter.GetComponent<PlayerNetworkID>().id = newPlayer.player.id;
            playerCharacterList.Add(newPlayer.player.id, peerPlayerCharacter);
        }
    }

    void UpdatePlayers(){
        foreach (Player p in latestGameState.players)
        {
            // Update position of every other character
            if (p.id != ownPlayerCharacter.id)
            {
                playerCharacterList[p.id].GetComponent<PlayerNetworkID>().id = p.id;
                Vector3 diff = transform.TransformDirection(new Vector3(p.position.X, p.position.Y, p.position.Z) - playerCharacterList[p.id].transform.position);
                playerCharacterList[p.id].GetComponent<CharacterController>().Move(diff * Time.deltaTime);
            }
        }
    }

    void DestroyPlayers()
    {
        // There are more characters than there are players, people have been dropped
        if (playerCharacterList.Count > latestGameState.players.Length)
        {
            foreach (Player p in latestDroppedPlayers.players)
            {
                Destroy(playerCharacterList[p.id]);
                playerCharacterList.Remove(p.id);
            }
        }
    }
    
    void HeartBeat(){

        if (ownPlayerCharacter != null)
        {
            GameObject character = playerCharacterList[ownPlayerCharacter.id];

            Byte[] sendBytes = Encoding.ASCII.GetBytes("heartbeat;position=" +
                    character.transform.position.x + "," + character.transform.position.y + "," + character.transform.position.z + ";");
            udp.Send(sendBytes, sendBytes.Length);
            udp.Send(sendBytes, sendBytes.Length);
        }
    }

    void Update(){
        SpawnPlayers();
        UpdatePlayers();
        DestroyPlayers();
    }
}
