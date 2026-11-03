#import <AuthenticationServices/AuthenticationServices.h>
#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>

typedef void (*PlayServAppleCredentialCallback)(
    int requestId,
    int credentialType,
    const char *userId,
    const char *email,
    const char *fullName,
    const char *givenName,
    const char *familyName,
    const char *identityToken,
    const char *authorizationCode,
    const char *state,
    const char *password);

typedef void (*PlayServAppleErrorCallback)(int requestId, int code, const char *message);
typedef void (*PlayServAppleCredentialStateCallback)(int requestId, int state, int errorCode, const char *errorMessage);
typedef void (*PlayServAppleRevokedCallback)();

static NSString *PlayServStringFromCString(const char *value)
{
    if (value == NULL)
        return @"";

    return [NSString stringWithUTF8String:value] ?: @"";
}

static const char *PlayServCString(NSString *value)
{
    return value == nil ? "" : [value UTF8String];
}

static NSString *PlayServStringFromData(NSData *data)
{
    if (data == nil || data.length == 0)
        return @"";

    return [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] ?: @"";
}

static UIWindow *PlayServPresentationWindow()
{
    if (@available(iOS 13.0, *))
    {
        for (UIScene *scene in UIApplication.sharedApplication.connectedScenes)
        {
            if (scene.activationState != UISceneActivationStateForegroundActive ||
                ![scene isKindOfClass:[UIWindowScene class]])
            {
                continue;
            }

            UIWindowScene *windowScene = (UIWindowScene *)scene;
            for (UIWindow *window in windowScene.windows)
            {
                if (window.isKeyWindow)
                    return window;
            }
        }
    }

    return UIApplication.sharedApplication.keyWindow;
}

static NSMutableDictionary<NSNumber *, id> *PlayServAppleRequests()
{
    static NSMutableDictionary<NSNumber *, id> *requests = nil;
    static dispatch_once_t onceToken;
    dispatch_once(&onceToken, ^{
        requests = [[NSMutableDictionary alloc] init];
    });

    return requests;
}

@interface PlayServAppleAuthorizationRequest : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property(nonatomic, assign) int requestId;
@property(nonatomic, assign) PlayServAppleCredentialCallback successCallback;
@property(nonatomic, assign) PlayServAppleErrorCallback errorCallback;
@property(nonatomic, strong) ASAuthorizationController *controller;
- (instancetype)initWithRequestId:(int)requestId
                  successCallback:(PlayServAppleCredentialCallback)successCallback
                    errorCallback:(PlayServAppleErrorCallback)errorCallback;
- (void)performWithRequests:(NSArray<ASAuthorizationRequest *> *)requests;
@end

@implementation PlayServAppleAuthorizationRequest
- (instancetype)initWithRequestId:(int)requestId
                  successCallback:(PlayServAppleCredentialCallback)successCallback
                    errorCallback:(PlayServAppleErrorCallback)errorCallback
{
    self = [super init];
    if (self)
    {
        self.requestId = requestId;
        self.successCallback = successCallback;
        self.errorCallback = errorCallback;
    }

    return self;
}

- (void)performWithRequests:(NSArray<ASAuthorizationRequest *> *)requests
{
    self.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:requests];
    self.controller.delegate = self;
    self.controller.presentationContextProvider = self;
    PlayServAppleRequests()[@(self.requestId)] = self;
    [self.controller performRequests];
}

- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller API_AVAILABLE(ios(13.0))
{
    UIWindow *window = PlayServPresentationWindow();
    return window ?: [[UIWindow alloc] initWithFrame:UIScreen.mainScreen.bounds];
}

- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithAuthorization:(ASAuthorization *)authorization API_AVAILABLE(ios(13.0))
{
    id credential = authorization.credential;
    if ([credential isKindOfClass:[ASAuthorizationAppleIDCredential class]])
    {
        ASAuthorizationAppleIDCredential *appleCredential = (ASAuthorizationAppleIDCredential *)credential;
        NSPersonNameComponentsFormatter *formatter = [[NSPersonNameComponentsFormatter alloc] init];
        NSString *fullName = appleCredential.fullName == nil ? @"" : [formatter stringFromPersonNameComponents:appleCredential.fullName];
        NSString *givenName = appleCredential.fullName.givenName ?: @"";
        NSString *familyName = appleCredential.fullName.familyName ?: @"";
        NSString *identityToken = PlayServStringFromData(appleCredential.identityToken);
        NSString *authorizationCode = PlayServStringFromData(appleCredential.authorizationCode);

        if (self.successCallback != NULL)
        {
            self.successCallback(
                self.requestId,
                1,
                PlayServCString(appleCredential.user),
                PlayServCString(appleCredential.email),
                PlayServCString(fullName),
                PlayServCString(givenName),
                PlayServCString(familyName),
                PlayServCString(identityToken),
                PlayServCString(authorizationCode),
                PlayServCString(appleCredential.state),
                "");
        }
    }
    else if ([credential isKindOfClass:[ASPasswordCredential class]])
    {
        ASPasswordCredential *passwordCredential = (ASPasswordCredential *)credential;
        if (self.successCallback != NULL)
        {
            self.successCallback(
                self.requestId,
                2,
                PlayServCString(passwordCredential.user),
                "",
                "",
                "",
                "",
                "",
                "",
                "",
                PlayServCString(passwordCredential.password));
        }
    }
    else if (self.errorCallback != NULL)
    {
        self.errorCallback(self.requestId, -2, "Unsupported Apple authorization credential type.");
    }

    [PlayServAppleRequests() removeObjectForKey:@(self.requestId)];
}

- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithError:(NSError *)error API_AVAILABLE(ios(13.0))
{
    if (self.errorCallback != NULL)
    {
        self.errorCallback(
            self.requestId,
            (int)error.code,
            PlayServCString(error.localizedDescription));
    }

    [PlayServAppleRequests() removeObjectForKey:@(self.requestId)];
}
@end

static ASAuthorizationAppleIDRequest *PlayServCreateAppleIdRequest(int scopes, const char *nonce, const char *state)
{
    ASAuthorizationAppleIDProvider *provider = [[ASAuthorizationAppleIDProvider alloc] init];
    ASAuthorizationAppleIDRequest *request = [provider createRequest];

    NSMutableArray *requestedScopes = [[NSMutableArray alloc] init];
    if ((scopes & 1) != 0)
        [requestedScopes addObject:ASAuthorizationScopeEmail];
    if ((scopes & 2) != 0)
        [requestedScopes addObject:ASAuthorizationScopeFullName];

    request.requestedScopes = requestedScopes;

    NSString *nonceString = PlayServStringFromCString(nonce);
    if (nonceString.length > 0)
        request.nonce = nonceString;

    NSString *stateString = PlayServStringFromCString(state);
    if (stateString.length > 0)
        request.state = stateString;

    return request;
}

extern "C" bool PlayServAppleSignIn_IsSupported()
{
    if (@available(iOS 13.0, *))
        return true;

    return false;
}

extern "C" void PlayServAppleSignIn_SignIn(
    int requestId,
    int scopes,
    const char *nonce,
    const char *state,
    PlayServAppleCredentialCallback successCallback,
    PlayServAppleErrorCallback errorCallback)
{
    if (!PlayServAppleSignIn_IsSupported())
    {
        if (errorCallback != NULL)
            errorCallback(requestId, -1, "Apple Sign In requires iOS 13 or newer.");
        return;
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        PlayServAppleAuthorizationRequest *request =
            [[PlayServAppleAuthorizationRequest alloc] initWithRequestId:requestId
                                                         successCallback:successCallback
                                                           errorCallback:errorCallback];
        [request performWithRequests:@[PlayServCreateAppleIdRequest(scopes, nonce, state)]];
    });
}

extern "C" void PlayServAppleSignIn_QuickLogin(
    int requestId,
    int scopes,
    const char *nonce,
    const char *state,
    PlayServAppleCredentialCallback successCallback,
    PlayServAppleErrorCallback errorCallback)
{
    if (!PlayServAppleSignIn_IsSupported())
    {
        if (errorCallback != NULL)
            errorCallback(requestId, -1, "Apple Sign In requires iOS 13 or newer.");
        return;
    }

    dispatch_async(dispatch_get_main_queue(), ^{
        ASAuthorizationPasswordProvider *passwordProvider = [[ASAuthorizationPasswordProvider alloc] init];
        PlayServAppleAuthorizationRequest *request =
            [[PlayServAppleAuthorizationRequest alloc] initWithRequestId:requestId
                                                         successCallback:successCallback
                                                           errorCallback:errorCallback];
        [request performWithRequests:@[
            PlayServCreateAppleIdRequest(scopes, nonce, state),
            [passwordProvider createRequest]
        ]];
    });
}

extern "C" void PlayServAppleSignIn_GetCredentialState(
    int requestId,
    const char *userId,
    PlayServAppleCredentialStateCallback callback)
{
    if (callback == NULL)
        return;

    if (!PlayServAppleSignIn_IsSupported())
    {
        callback(requestId, 0, -1, "Apple Sign In requires iOS 13 or newer.");
        return;
    }

    NSString *userIdString = PlayServStringFromCString(userId);
    if (userIdString.length == 0)
    {
        callback(requestId, 0, -2, "Apple user id is required.");
        return;
    }

    ASAuthorizationAppleIDProvider *provider = [[ASAuthorizationAppleIDProvider alloc] init];
    [provider getCredentialStateForUserID:userIdString completion:^(ASAuthorizationAppleIDProviderCredentialState credentialState, NSError *error) {
        dispatch_async(dispatch_get_main_queue(), ^{
            int errorCode = error == nil ? 0 : (int)error.code;
            const char *errorMessage = error == nil ? "" : PlayServCString(error.localizedDescription);
            callback(requestId, (int)credentialState, errorCode, errorMessage);
        });
    }];
}

extern "C" void PlayServAppleSignIn_SetCredentialsRevokedCallback(PlayServAppleRevokedCallback callback)
{
    static id observer = nil;

    dispatch_async(dispatch_get_main_queue(), ^{
        if (observer != nil)
        {
            [[NSNotificationCenter defaultCenter] removeObserver:observer];
            observer = nil;
        }

        if (callback == NULL || !PlayServAppleSignIn_IsSupported())
            return;

        observer = [[NSNotificationCenter defaultCenter]
            addObserverForName:ASAuthorizationAppleIDProviderCredentialRevokedNotification
                        object:nil
                         queue:[NSOperationQueue mainQueue]
                    usingBlock:^(NSNotification *notification) {
                        callback();
                    }];
    });
}
