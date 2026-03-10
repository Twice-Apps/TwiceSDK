using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class GetUserIdentifier : MonoBehaviour
{
    /*
    //for android, get gaid and for ios get idfa    
    void Start()
    {
        string id = "";
        #if UNITY_ANDROID
            id = GetAndroidId();
        #elif UNITY_IOS
            id = GetIOSId();
        #endif
        Debug.Log("User Identifier: " + id);
    }   
    */
    
    public static string GetAndroidId()
    {
        AndroidJavaClass up = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
        AndroidJavaObject currentActivity = up.GetStatic<AndroidJavaObject>("currentActivity");
        AndroidJavaObject contentResolver = currentActivity.Call<AndroidJavaObject>("getContentResolver");
        AndroidJavaClass secure = new AndroidJavaClass("android.provider.Settings$Secure");
        string androidId = secure.CallStatic<string>("getString", contentResolver, "android_id");
        return androidId;
    }
    
    public static string GetIOSId()
    {
        #if UNITY_IOS
        return UnityEngine.iOS.Device.advertisingIdentifier;
        #endif
        return "failed";
    }
    
    public static string GetAndroidAdvertiserId()
    {
        string advertisingID = "";
        try
        {
            AndroidJavaClass up = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            AndroidJavaObject currentActivity = up.GetStatic<AndroidJavaObject>("currentActivity");
            AndroidJavaClass client = new AndroidJavaClass("com.google.android.gms.ads.identifier.AdvertisingIdClient");
            AndroidJavaObject adInfo = client.CallStatic<AndroidJavaObject>("getAdvertisingIdInfo", currentActivity);
            advertisingID = adInfo.Call<string>("getId").ToString();
        }
        catch (System.Exception)
        {
            Debug.Log("Couldn't get GAID!");
        }
        return advertisingID;
    }

    public static string GetGaid()
    {
    #if UNITY_ANDROID
        return GetAndroidAdvertiserId();
    #elif UNITY_IOS
            return GetIOSId();
    #endif
        return null;
    }
}
