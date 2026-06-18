#import <Foundation/Foundation.h>

// Returns 1 if the running build uses a SANDBOX App Store receipt (TestFlight or Xcode/StoreKit
// sandbox), 0 otherwise (production App Store) or if no receipt is present.
// TestFlight always carries a receipt named "sandboxReceipt"; production builds carry "receipt".
extern "C" int _TwiceIsSandboxReceipt()
{
    NSURL *receiptURL = [[NSBundle mainBundle] appStoreReceiptURL];
    if (receiptURL == nil) {
        return 0;
    }
    NSString *last = [receiptURL lastPathComponent];
    return (last != nil && [last isEqualToString:@"sandboxReceipt"]) ? 1 : 0;
}
