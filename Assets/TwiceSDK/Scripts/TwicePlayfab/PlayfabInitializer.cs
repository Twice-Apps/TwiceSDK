using System.Linq;
using UnityEngine;
using PlayFab;
using PlayFab.ClientModels;

public class PlayfabInitializer : MonoBehaviour
{
    private void Start() => Login();
    
    #region Login
    
    private void Login()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        //Get the device id from native android
        AndroidJavaClass up = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject currentActivity = up.GetStatic<AndroidJavaObject>("currentActivity");
        AndroidJavaObject contentResolver = currentActivity.Call<AndroidJavaObject>("getContentResolver");
        AndroidJavaClass secure = new AndroidJavaClass("android.provider.Settings$Secure");
        string deviceId = secure.CallStatic<string>("getString", contentResolver, "android_id");

        //Login with the android device ID
        PlayFabClientAPI.LoginWithAndroidDeviceID(new LoginWithAndroidDeviceIDRequest() {
            TitleId = PlayFabSettings.TitleId,
            AndroidDevice = SystemInfo.deviceModel,
            OS = SystemInfo.operatingSystem,
            AndroidDeviceId = deviceId,
            CreateAccount = true
        }, OnLoginSuccess,OnError);
        
#elif UNITY_IPHONE || UNITY_IOS && !UNITY_EDITOR
        PlayFabClientAPI.LoginWithIOSDeviceID(new LoginWithIOSDeviceIDRequest() {
            TitleId = PlayFabSettings.TitleId,
            DeviceModel = SystemInfo.deviceModel,
            OS = SystemInfo.operatingSystem,
            DeviceId = SystemInfo.deviceUniqueIdentifier,
            CreateAccount = true
        }, OnLoginSuccess, OnError);
#else
        PlayFabClientAPI.LoginWithCustomID(new LoginWithCustomIDRequest()
        {
            TitleId = PlayFabSettings.TitleId,
            CustomId = SystemInfo.deviceUniqueIdentifier,
            CreateAccount = true
        }, OnLoginSuccess, OnError);
#endif
    }
    
    
    void LoginOld()
    {
        var request = new LoginWithCustomIDRequest
        {
            CustomId = SystemInfo.deviceUniqueIdentifier,
            CreateAccount = true
        };

        PlayFabClientAPI.LoginWithCustomID(request, OnLoginSuccess, OnError);
    }
    
    #endregion
    
    void OnLoginSuccess(LoginResult result)
    {
        if (result != null)
        {
            PlayfabManager.Instance.playerId = result.PlayFabId;
            PlayfabManager.Instance.Init();
            Debug.Log("<color=Green>(Playfab) Succesfull Login!</color>");
        }
        else
        {
            Debug.Log("Failed to login.");
        }
    }

    protected void OnError(PlayFabError error)
    {
        Debug.Log("<color=Red>(Playfab) Error : </color> "+ error.ErrorMessage);
    }

    void OnError(LoginResult r)
    {
        
    }

    public void UpdateUserTitleDisplayName(string targetName)
    {
        string newName = GetLast25Characters(targetName);
        
        #if UNITY_EDITOR
        newName = "UNITY_EDITOR";
        #endif
        
        Debug.Log(newName);
        
        var request = new UpdateUserTitleDisplayNameRequest
        {
            DisplayName = newName
        };
        
        PlayFabClientAPI.UpdateUserTitleDisplayName(request, (x)=>
            {Debug.Log("<color=Green>(Playfab) Updated Display Name</color>");}, (f)=>
            {Debug.Log("<color=Green>(Playfab) Couldn't Update Display Name!</color>"+ f.ErrorMessage);});
    }
    public string GetLast25Characters(string input)
    {
        if (input.Length <= 25)
        {
            return input; 
        }
        else
        {
            return input.Substring(input.Length - 25); 
        }
    }
}
