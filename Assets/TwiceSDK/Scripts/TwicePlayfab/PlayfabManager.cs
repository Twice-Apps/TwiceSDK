using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using PlayFab;
using PlayFab.ClientModels;
using Newtonsoft.Json;
using Playfab_Properties;
using VInspector;

public class PlayfabManager : PlayfabInitializer
{
    #region Singleton
    public static PlayfabManager Instance { get; set; }
    private void Awake()
    {
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    #endregion

    [Tab("PlayerInfo")]
    public string playerId;
    public string playerCustomId;
    public int playerCoins;
    public int playerLevel;
    
    [Tab("Properties")]
    public string[] availableVersions;
    [EndTab] [Tab("Settings")]
    public GameSettings currentGameSettings;

    [EndTab]
    
    public PlayfabCurrency playfabCurrency;
    private bool isInit = false;
    public void Init()
    {
        playerCustomId = SystemInfo.deviceUniqueIdentifier;
        GetTitleData();
        playfabCurrency.GetVirtualCurrencies();
    }
    
    public void GetTitleData()
    {
        PlayFabClientAPI.GetTitleData(new GetTitleDataRequest(), OnTitleDataReceived, OnError);
    }
    
    private void CheckVersion()
    {
        var v = VersionChecker.Instance;

        foreach (var version in availableVersions)
        {
            if (version == v.version)
            {
                Debug.Log("<color=$FFFFFF>Game is up to date!</color>");
                Destroy(VersionChecker.Instance.gameObject);
                Loading.Instance.isDataRetrieved = true;
                return;
            }
        }
        
        VersionChecker.Instance.OpenCanvas();
    }
    
    private string GetLast25Digits(string input)
    {
        if (input.Length <= 25)
        {
            return input;
        }
        return input.Substring(input.Length - 25);
    }
    
    private void OnTitleDataReceived(GetTitleDataResult result)
    {
        if (result.Data == null) return;
        
        if (result.Data.ContainsKey("GameSettings"))
        {
            currentGameSettings = JsonConvert.DeserializeObject<GameSettings>(result.Data["GameSettings"]);
        }
        
        
        if (result.Data.ContainsKey("VersionChecker"))
        {
            string jsonString = result.Data["VersionChecker"];
            availableVersions = JsonConvert.DeserializeObject<string[]>(jsonString);
        }


        CheckVersion();
        SetGameGlobalSettings();
        //isStartedChecking = true;
        Debug.Log("<color=$FFFFFF>(Playfab) Title Data Received!</color>");
    }

    void SetGameGlobalSettings()
    {
        //livesSettings.maxLivesCount = currentGameSettings.maxLivesCount;
        //livesSettings.oneLifeRestorationDuration = currentGameSettings.oneLifeRestorationDuration;
    }
    
    public void SendLeaderboard(int score)
    {
        var request = new UpdatePlayerStatisticsRequest
        {
            Statistics = new List<StatisticUpdate>
            {
                new StatisticUpdate
                {
                    StatisticName = "LeaderboardStatistics",
                    Value = score
                }
            }
        };
        PlayFabClientAPI.UpdatePlayerStatistics(request,OnLeaderboardUpdated,OnError);
    }

    private void OnLeaderboardUpdated(UpdatePlayerStatisticsResult result)
    {
        Debug.Log("Leaderboard Sent Succesfully!");
    }

    public void GetLeaderboard()
    {
        var request = new GetLeaderboardRequest()
        {
            StatisticName = "LeaderboardStatistics",
            StartPosition = 0,
            MaxResultsCount = 10
        };
        PlayFabClientAPI.GetLeaderboard(request,OnLeaderboardGet,OnError);
        
        // Mevcut oyuncunun sıralamasını çekiyoruz.
        var playerLeaderboardRequest = new GetLeaderboardAroundPlayerRequest()
        {
            StatisticName = "LeaderboardStatistics",
            PlayFabId = playerId, // Giriş sırasında saklanan oyuncu id'si.
            MaxResultsCount = 1 // Sadece oyuncuya ait entry yeterlidir.
        };
        PlayFabClientAPI.GetLeaderboardAroundPlayer(playerLeaderboardRequest, OnLeaderboardAroundPlayerGet, OnError);

    }
    
    public List<PlayerLeaderboardEntry> leaderboardEntries;
    public PlayerLeaderboardEntry playerEntry;
    
    public TimeSpan remainingTime;
    public DateTime nextResetDate;

    private void OnLeaderboardGet(GetLeaderboardResult result)
    {
        leaderboardEntries = result.Leaderboard;
        //EventManager.OnLeaderboardGet?.Invoke();
        foreach (var item in result.Leaderboard)
        {
            Debug.Log(item.Position + " " + item.PlayFabId + " " + item.StatValue);
        }

        if (result.NextReset.HasValue)
        {
            nextResetDate = result.NextReset.Value;
            remainingTime = nextResetDate - DateTime.UtcNow;
        }
    }
    private void OnLeaderboardAroundPlayerGet(GetLeaderboardAroundPlayerResult result)
    {
        // Eğer oyuncunun entry'si top 10'da yoksa, ekleyelim.
        if (result.Leaderboard != null && result.Leaderboard.Count > 0)
        {
            var p = result.Leaderboard[0];
            bool exists = leaderboardEntries.Any(entry => entry.PlayFabId == playerEntry.PlayFabId);
            if (!exists)
            {
                playerEntry = p;
                Debug.Log("Player leaderboard entry'si eklendi: " + playerEntry.PlayFabId + " " + playerEntry.StatValue);
            }
        }
    }
}

[Serializable]
public class AreaUnlockOrder
{
    public string areaName;
    public int order;
}
