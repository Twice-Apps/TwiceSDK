using Playfab_Interface_Currency;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Playfab_Currency
{
    public class Coin : ICurrency
    {
        private ICurrency _iCurrencyImplementation;
        private const string COIN = "CN";
        public string CurrencyKey() => COIN;
        
        public void Get()
        {
            PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), (result) =>
            {
                int _coins = result.VirtualCurrency[COIN];
        
                PlayfabManager.Instance.playerCoins = _coins;
        
                /*
                if (LevelManager.Instance != null)
                {
                    LevelManager.Instance.SetLevel();
                    //LevelManager.Instance.currentLevel = playerLevel;
                    //UIManager.Instance.LevelSet();
                }
                */
                
                Debug.Log("<color=$38FF32>(Playfab) Succesfully Retrieved Coins!</color>");
            },OnError);
        }
        public void Add(int amount)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;

            var request = new AddUserVirtualCurrencyRequest()
            {
                VirtualCurrency = COIN,
                Amount = amount
            };
        
            PlayFabClientAPI.AddUserVirtualCurrency(request,OnSuccess,OnError);
        }
        public void Substract(int amount)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
            
            var request = new SubtractUserVirtualCurrencyRequest()
            {
                VirtualCurrency = COIN,
                Amount = amount
            };
            PlayFabClientAPI.SubtractUserVirtualCurrency(request,OnSuccess,OnError);
        }
        
        private void OnSuccess(ModifyUserVirtualCurrencyResult obj)
        {
            PlayfabManager.Instance.playerCoins = obj.Balance;
            EventManager.OnCoinsChanged?.Invoke();
            Debug.Log("<color=$38FF32>(Playfab) Coins succesfully changed.</color>");
        }

        public void OnError(PlayFabError error) => Debug.Log("<color=$FF5858>(Playfab) Error, Coins couldn't retrieved.</color>");
    }
}