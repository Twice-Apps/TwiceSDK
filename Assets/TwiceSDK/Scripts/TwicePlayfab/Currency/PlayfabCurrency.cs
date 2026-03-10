using System;
using Playfab_Currency;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

[Serializable]
public class PlayfabCurrency
{
    public Coin coins = new();
    public Level level = new();
    
    public void GetVirtualCurrencies()
    {
        PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), (result) =>
        {
            int _coins = result.VirtualCurrency[coins.CurrencyKey()];
            int _level = result.VirtualCurrency[level.CurrencyKey()];
            
            PlayfabManager.Instance.playerCoins = _coins;
            PlayfabManager.Instance.playerLevel = _level;
            
            Debug.Log("<color=$FFFFFF>(Playfab) Succesfully Retrieved Virtual Currencies!</color>");
        },OnError);
    }
    
    private void OnError(PlayFabError error)
    {
        Debug.Log("<color=Red>(Playfab) Error : </color> "+ error.ErrorMessage);
    }
}