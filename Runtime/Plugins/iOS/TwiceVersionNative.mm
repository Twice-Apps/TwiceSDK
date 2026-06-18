#import <Foundation/Foundation.h>
#include <stdlib.h>
#include <string.h>

// Returns the iOS build number (CFBundleVersion, e.g. "1"), which the store increments on every
// App Store / TestFlight upload even when the marketing version (CFBundleShortVersionString)
// stays the same. The C# caller marshals the returned UTF-8 string (and owns the copy).
extern "C" const char* _TwiceBuildNumber()
{
    NSString *build = [[NSBundle mainBundle] objectForInfoDictionaryKey:@"CFBundleVersion"];
    if (build == nil) {
        return strdup("");
    }
    return strdup([build UTF8String]);
}
