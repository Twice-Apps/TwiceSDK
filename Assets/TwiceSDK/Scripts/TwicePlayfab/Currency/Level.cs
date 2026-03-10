using Playfab_Interface_Currency;
using PlayFab;
using PlayFab.ClientModels;
using UnityEngine;

namespace Playfab_Currency
{
    public class Level : ICurrency
    {
        private const string LEVEL = "LV";
        public string CurrencyKey() => LEVEL;

        public void Get()
        {
            PlayFabClientAPI.GetUserInventory(new GetUserInventoryRequest(), (result) =>
            {
                int _level = result.VirtualCurrency[LEVEL];
        
                PlayfabManager.Instance.playerLevel = _level;
                
                Debug.Log("<color=$38FF32>(Playfab) Succesfully Retrieved Level!</color>");
            },OnError);
        }
        public void Add(int amount)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
            
            var request = new AddUserVirtualCurrencyRequest()
            {
                VirtualCurrency = LEVEL,
                Amount = amount
            };
        
            PlayFabClientAPI.AddUserVirtualCurrency(request,OnSuccess,OnError);
        }
        public void TargetLevel(int targetLevel)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
        
            int levelToAdd = targetLevel - (PlayfabManager.Instance.playerLevel);

            if (levelToAdd <= 0) return;

            var request = new AddUserVirtualCurrencyRequest()
            {
                VirtualCurrency = LEVEL,
                Amount = levelToAdd
            };
            PlayFabClientAPI.AddUserVirtualCurrency(request,OnSuccess,OnError);
        
            Debug.Log($"<color=green>LevelUp_{levelToAdd}</color>");
        }
        public void Substract(int amount)
        {
            if (!PlayFabClientAPI.IsClientLoggedIn()) return;
            
            var request = new SubtractUserVirtualCurrencyRequest()
            {
                VirtualCurrency = LEVEL,
                Amount = amount
            };
            PlayFabClientAPI.SubtractUserVirtualCurrency(request,OnSuccess,OnError);
        }
        
        private void OnSuccess(ModifyUserVirtualCurrencyResult obj)
        {
            PlayfabManager.Instance.playerLevel = obj.Balance;
            EventManager.OnLevelChanged?.Invoke();
            Debug.Log("<color=$38FF32>(Playfab) Level succesfully changed.</color>");
        }

        private void OnError(PlayFabError obj) => Debug.Log("<color=$FF5858>(Playfab) Error, Level couldn't retrieved.</color>");
    }
}