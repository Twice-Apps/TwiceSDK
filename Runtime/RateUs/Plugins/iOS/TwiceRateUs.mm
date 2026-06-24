#import <StoreKit/StoreKit.h>
#import <UIKit/UIKit.h>

// Triggers the native iOS in-app review (the system star sheet). Apple throttles it
// (a few times/year, not guaranteed to show, no callback) — this is by design.
extern "C" void _twiceRequestReview()
{
    if (@available(iOS 14.0, *))
    {
        UIWindowScene *scene = nil;
        for (UIScene *s in UIApplication.sharedApplication.connectedScenes)
        {
            if ([s isKindOfClass:[UIWindowScene class]] &&
                s.activationState == UISceneActivationStateForegroundActive)
            {
                scene = (UIWindowScene *)s;
                break;
            }
        }
        if (scene != nil)
        {
            [SKStoreReviewController requestReviewInScene:scene];
            return;
        }
    }
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
    [SKStoreReviewController requestReview];
#pragma clang diagnostic pop
}
