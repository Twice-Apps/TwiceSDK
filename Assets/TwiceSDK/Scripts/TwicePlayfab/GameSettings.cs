
namespace Playfab_Properties
{
    [System.Serializable]
    public class GameSettings
    {
        public bool isLeaderboardActive;
        public int maxLivesCount;
        public int oneLifeRestorationDuration;
        public bool isAdsActive;
        public string newspaperLink;
        public bool karnavalMusic;
        public int fortuneSpinHoursPerSpin;
        public bool countdown;
        public string countdownText;
    
        //Constructor
        public GameSettings(bool _isLeaderboardActive,int _maxLivesCount,int _oneLifeRestorationDuration,bool _isAdsActive,string _newspaperLink,bool _karnavalMusic,int _fortuneSpinHoursPerSpin,bool _countdown,string _countdownText)
        {
            isLeaderboardActive = _isLeaderboardActive;
            maxLivesCount = _maxLivesCount;
            oneLifeRestorationDuration = _oneLifeRestorationDuration;
            isAdsActive = _isAdsActive;
            newspaperLink = _newspaperLink;
            karnavalMusic = _karnavalMusic;
            fortuneSpinHoursPerSpin = _fortuneSpinHoursPerSpin;
            countdown = _countdown;
            countdownText = _countdownText;
        }
    }
}