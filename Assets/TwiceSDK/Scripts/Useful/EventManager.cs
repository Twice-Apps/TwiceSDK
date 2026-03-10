using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EventManager
{
    public static Action<Popup> OnPopupOpened;
    public static Action<Popup> OnPopupClosed;
    
    public static Action OnCoinsChanged;
    public static Action OnStarsChanged;
    public static Action OnLevelChanged;
}
