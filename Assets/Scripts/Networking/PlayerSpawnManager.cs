using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class PlayerSpawnManager : NetworkBehaviour
{
    public static PlayerSpawnManager Instance { get; private set; }

    [SerializeField] private GameObject renkliTeamPrefab;
    [SerializeField] private GameObject renksizTeamPrefab;
    [SerializeField] private Transform renkliTeamSpawnParent; // child: 1, 2
    [SerializeField] private Transform renksizTeamSpawnParent; // child: 1, 2

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        SpawnAllPlayers();
    }

    private void SpawnAllPlayers()
    {
        var renkliSpawns = GetChildren(renkliTeamSpawnParent);
        var renksizSpawns = GetChildren(renksizTeamSpawnParent);
        Shuffle(renkliSpawns);
        Shuffle(renksizSpawns);

        int rIdx = 0, rsIdx = 0;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            // Garanti olması adına, oyuncunun hali hazırda bir karakteri varsa önce onu siliyoruz
            if (client.PlayerObject != null)
            {
                client.PlayerObject.Despawn(true);
            }

            int team = GetTeamForClient(client.ClientId);
            GameObject prefab;
            Vector3 spawnPos = Vector3.zero; // Eğer spawn noktası yoksa haritanın ortasına atsın
            Quaternion spawnRot = Quaternion.identity;

            if (team == 2)
            {
                prefab = renksizTeamPrefab;
                if (renksizSpawns.Count > 0)
                {
                    var spawn = renksizSpawns[rsIdx % renksizSpawns.Count];
                    spawnPos = spawn.position;
                    spawnRot = spawn.rotation;
                }
                rsIdx++;
            }
            else
            {
                prefab = renkliTeamPrefab;
                if (renkliSpawns.Count > 0)
                {
                    var spawn = renkliSpawns[rIdx % renkliSpawns.Count];
                    spawnPos = spawn.position;
                    spawnRot = spawn.rotation;
                }
                rIdx++;
            }

            var go = Instantiate(prefab, spawnPos, spawnRot);
            var netObj = go.GetComponent<NetworkObject>();
            if (netObj == null) { Debug.LogError($"[Spawn] {prefab.name} prefab'ında NetworkObject yok!"); Destroy(go); continue; }
            netObj.SpawnAsPlayerObject(client.ClientId);

            // ClientNetworkTransform kullanıldığında istemcinin (0,0,0) noktasında doğup aşağı düşmesini engellemek için
            if (go.TryGetComponent(out PlayerHealth health))
            {
                health.Respawn(spawnPos, spawnRot);
            }
        }
    }

    private static int GetTeamForClient(ulong clientId)
    {
        if (NetworkLobbyManager.Instance == null) return 1;

        foreach (var player in NetworkLobbyManager.Instance.LobbyPlayers)
        {
            if (player.ClientId == clientId)
                return player.TeamId;
        }
        return 1;
    }

    private static List<Transform> GetChildren(Transform parent)
    {
        var list = new List<Transform>();
        foreach (Transform child in parent)
            list.Add(child);
        return list;
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // GameManager'ın ölen oyuncuyu doğru noktada doğurabilmesi için
    public (Vector3 position, Quaternion rotation) GetRandomSpawnPoint(int teamId)
    {
        var spawns = teamId == 2 ? GetChildren(renksizTeamSpawnParent) : GetChildren(renkliTeamSpawnParent);
        if (spawns.Count == 0) return (Vector3.zero, Quaternion.identity);
        
        Transform spawn = spawns[Random.Range(0, spawns.Count)];
        return (spawn.position, spawn.rotation);
    }
}
