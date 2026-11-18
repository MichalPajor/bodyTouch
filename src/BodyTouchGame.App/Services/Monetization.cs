using Android.BillingClient.Api;
using Android.Gms.Ads;
using Android.Gms.Ads.Interstitial;
using Google.Android.Play.Core.Review;
using Xamarin.Google.UserMesssagingPlatform;
using Activity = Android.App.Activity;

namespace BodyTouchGame.App.Services;

// Stage 11 (decision D10): AdMob interstitials every ~6 rounds between rounds, the one-time purchase "remove_ads"
// (Google Play Billing) and the Google Play in-app review after the 3rd finished game.

/// <summary>Whether the player bought "remove_ads" (kept in preferences, refreshed from Google Play on start).</summary>
public sealed class PremiumState(IPreferences preferences)
{
    private const string Key = "shop.removeAds";

    public event EventHandler? Changed;

    public bool AdsRemoved
    {
        get => preferences.Get(Key, false);
        set
        {
            if (value == AdsRemoved) return;
            preferences.Set(Key, value);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

/// <summary>
/// Full-screen ads (AdMob interstitial) – only between rounds, never during touching, the curtain or guessing,
/// at most once every <see cref="RoundsBetweenAds"/> finished rounds (counted across games), never after the
/// purchase. Asks for consent first (Google UMP, required for AdMob in the EEA); without consent or network the
/// game simply runs without ads.
/// </summary>
public sealed class AdService(IPreferences preferences, PremiumState premium)
{
    /// <summary>TEST unit from Google. Replace with the real interstitial unit id before publishing
    /// (together with the application id in AndroidManifest.xml).</summary>
    public const string InterstitialUnitId = "ca-app-pub-3940256099942544/1033173712";
    public const int RoundsBetweenAds = 6;
    private const string RoundsKey = "ads.roundsSinceAd";

    private InterstitialAd? _ad;
    private bool _loading;
    private bool _started;
    private bool _canRequestAds;

    public int RoundsSinceAd
    {
        get => preferences.Get(RoundsKey, 0);
        private set => preferences.Set(RoundsKey, value);
    }

    /// <summary>Consent (if needed) and ads SDK start-up; called once when the app starts.</summary>
    public void Start(Activity activity)
    {
        if (_started) return;
        _started = true;
        var consent = UserMessagingPlatform.GetConsentInformation(activity);
        var parameters = new ConsentRequestParameters.Builder().Build();
        consent.RequestConsentInfoUpdate(activity, parameters,
            new ConsentSuccess(() => UserMessagingPlatform.LoadAndShowConsentFormIfRequired(activity, new FormDismissed(_ => AfterConsent(activity, consent)))),
            new ConsentFailure(_ => AfterConsent(activity, consent)));
    }

    /// <summary>The ads privacy settings (Settings → Prywatność reklam).</summary>
    public void ShowPrivacyOptions(Activity activity) =>
        UserMessagingPlatform.ShowPrivacyOptionsForm(activity, new FormDismissed(_ => { }));

    /// <summary>A round was finished (counts towards the next ad).</summary>
    public void RoundFinished()
    {
        RoundsSinceAd++;
        if (RoundsSinceAd >= RoundsBetweenAds - 1) Preload();
    }

    /// <summary>Shows the ad if it is due and loaded; completes when the player closes it.</summary>
    public async Task ShowIfDueAsync()
    {
        if (premium.AdsRemoved || !_canRequestAds || RoundsSinceAd < RoundsBetweenAds) return;
        var ad = _ad;
        var activity = Platform.CurrentActivity;
        if (ad is null || activity is null)
        {
            Preload();   // not loaded yet (e.g. offline): try again after the next round
            return;
        }

        _ad = null;
        RoundsSinceAd = 0;
        var closed = new TaskCompletionSource();
        ad.FullScreenContentCallback = new ShowCallback(closed);
        ad.Show(activity);
        await Task.WhenAny(closed.Task, Task.Delay(TimeSpan.FromMinutes(3)));
        Preload();
    }

    private void AfterConsent(Activity activity, IConsentInformation consent)
    {
        _canRequestAds = consent.CanRequestAds();
        Console.WriteLine($"[BTG] ads: consent status={consent.ConsentStatus} canRequestAds={_canRequestAds}");
        if (!_canRequestAds) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MobileAds.Initialize(activity);
            Preload();
        });
    }

    private void Preload()
    {
        if (premium.AdsRemoved || !_canRequestAds || _ad is not null || _loading) return;
        _loading = true;
        MainThread.BeginInvokeOnMainThread(() =>
            InterstitialAd.Load(Platform.AppContext, InterstitialUnitId, new AdRequest.Builder().Build(), new LoadCallback(this)));
    }

    private sealed class LoadCallback(AdService owner) : TypedInterstitialLoadCallback
    {
        public override void OnInterstitialLoaded(InterstitialAd ad)
        {
            owner._loading = false;
            owner._ad = ad;
            Console.WriteLine("[BTG] ads: interstitial loaded");
        }

        public override void OnAdFailedToLoad(LoadAdError error)
        {
            owner._loading = false;
            Console.WriteLine($"[BTG] ads: load failed {error.Code} {error.Message}");
        }
    }

    private sealed class ShowCallback(TaskCompletionSource closed) : FullScreenContentCallback
    {
        public override void OnAdDismissedFullScreenContent() => closed.TrySetResult();

        public override void OnAdFailedToShowFullScreenContent(AdError error) => closed.TrySetResult();
    }

    private sealed class ConsentSuccess(Action done) : Java.Lang.Object, IConsentInformationOnConsentInfoUpdateSuccessListener
    {
        public void OnConsentInfoUpdateSuccess() => done();
    }

    private sealed class ConsentFailure(Action<FormError?> done) : Java.Lang.Object, IConsentInformationOnConsentInfoUpdateFailureListener
    {
        public void OnConsentInfoUpdateFailure(FormError? error) => done(error);
    }

    private sealed class FormDismissed(Action<FormError?> done) : Java.Lang.Object, IConsentFormOnConsentFormDismissedListener
    {
        public void OnConsentFormDismissed(FormError? error) => done(error);
    }
}

/// <summary>
/// The binding exposes onAdLoaded(T) of the generic Java callback as OnAdLoaded(Java.Lang.Object); overriding that
/// generates a Java method with the wrong signature ("name clash"). This base registers the real JNI signature
/// onAdLoaded(InterstitialAd) by hand (the usual workaround for this binding).
/// </summary>
public abstract class TypedInterstitialLoadCallback : InterstitialAdLoadCallback
{
    private static Delegate? _onAdLoaded;

    [Android.Runtime.Register("onAdLoaded", "(Lcom/google/android/gms/ads/interstitial/InterstitialAd;)V", "GetOnAdLoadedHandler")]
    public virtual void OnInterstitialLoaded(InterstitialAd ad)
    {
    }

    private static Delegate GetOnAdLoadedHandler() =>
        _onAdLoaded ??= Android.Runtime.JNINativeWrapper.CreateDelegate(new Action<IntPtr, IntPtr, IntPtr>(OnAdLoadedNative));

    private static void OnAdLoadedNative(IntPtr env, IntPtr self, IntPtr ad)
    {
        var callback = GetObject<TypedInterstitialLoadCallback>(env, self, Android.Runtime.JniHandleOwnership.DoNotTransfer);
        var interstitial = GetObject<InterstitialAd>(ad, Android.Runtime.JniHandleOwnership.DoNotTransfer);
        if (callback is not null && interstitial is not null) callback.OnInterstitialLoaded(interstitial);
    }
}

/// <summary>The one-time purchase "remove_ads" through Google Play Billing (buy, restore on start / on request).</summary>
public sealed class PurchaseService(PremiumState premium) : Java.Lang.Object, IPurchasesUpdatedListener
{
    public const string ProductId = "remove_ads";

    private BillingClient? _client;
    private ProductDetails? _product;
    private TaskCompletionSource<bool>? _purchase;

    /// <summary>Price from Google Play ("9,99 zł"), null until known.</summary>
    public string? Price { get; private set; }

    /// <summary>Connects, reads the price and restores an earlier purchase. False when Google Play is unavailable.</summary>
    public async Task<bool> RefreshAsync()
    {
        try
        {
            if (!await ConnectAsync()) return false;
            var query = QueryProductDetailsParams.NewBuilder()
                .SetProductList([QueryProductDetailsParams.Product.NewBuilder().SetProductId(ProductId).SetProductType(BillingClient.ProductType.Inapp).Build()])
                .Build();
            var details = await _client!.QueryProductDetailsAsync(query);
            _product = details.ProductDetails?.FirstOrDefault();
            Price = _product?.GetOneTimePurchaseOfferDetails()?.FormattedPrice;

            var purchases = await _client.QueryPurchasesAsync(QueryPurchasesParams.NewBuilder().SetProductType(BillingClient.ProductType.Inapp).Build());
            var owned = false;
            foreach (var purchase in purchases.Purchases ?? [])
            {
                if (purchase.Products.Contains(ProductId) && purchase.PurchaseState == PurchaseState.Purchased)
                {
                    owned = true;
                    await AcknowledgeAsync(purchase);
                }
            }

            if (owned) premium.AdsRemoved = true;
            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] billing: {e.Message}");
            return false;
        }
    }

    /// <summary>Opens the Google Play purchase sheet; true when the purchase went through.</summary>
    public async Task<bool> BuyAsync()
    {
        if (_product is null && !await RefreshAsync()) return false;
        if (_product is null || Platform.CurrentActivity is not { } activity) return false;
        _purchase = new TaskCompletionSource<bool>();
        var flow = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList([BillingFlowParams.ProductDetailsParams.NewBuilder().SetProductDetails(_product).Build()])
            .Build();
        var result = _client!.LaunchBillingFlow(activity, flow);
        if (result.ResponseCode != BillingResponseCode.Ok) return false;
        return await _purchase.Task;
    }

    public void OnPurchasesUpdated(BillingResult result, IList<Purchase>? purchases)
    {
        var bought = result.ResponseCode == BillingResponseCode.Ok &&
                     (purchases ?? []).Any(p => p.Products.Contains(ProductId) && p.PurchaseState == PurchaseState.Purchased);
        if (bought)
        {
            premium.AdsRemoved = true;
            foreach (var p in purchases!) _ = AcknowledgeAsync(p);
        }

        _purchase?.TrySetResult(bought);
    }

    private async Task AcknowledgeAsync(Purchase purchase)
    {
        if (purchase.IsAcknowledged || purchase.PurchaseState != PurchaseState.Purchased) return;
        await _client!.AcknowledgePurchaseAsync(AcknowledgePurchaseParams.NewBuilder().SetPurchaseToken(purchase.PurchaseToken).Build());
    }

    private async Task<bool> ConnectAsync()
    {
        _client ??= BillingClient.NewBuilder(Platform.AppContext)
            .SetListener(this)
            .EnablePendingPurchases(PendingPurchasesParams.NewBuilder().EnableOneTimeProducts().Build())
            .Build();
        if (_client.IsReady) return true;
        var connected = new TaskCompletionSource<bool>();
        _client.StartConnection(new StateListener(connected));
        return await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(10))) == connected.Task && connected.Task.Result;
    }

    private sealed class StateListener(TaskCompletionSource<bool> connected) : Java.Lang.Object, IBillingClientStateListener
    {
        public void OnBillingSetupFinished(BillingResult result) => connected.TrySetResult(result.ResponseCode == BillingResponseCode.Ok);

        public void OnBillingServiceDisconnected() => connected.TrySetResult(false);
    }
}

/// <summary>
/// Google Play in-app review after the 3rd finished game, then at most every 10 games (Google limits it further).
/// </summary>
public sealed class ReviewService(IPreferences preferences)
{
    private const string GamesKey = "review.gamesCompleted";
    private const string LastAskKey = "review.lastAskAtGame";
    public const int FirstAskAfterGames = 3;
    public const int GamesBetweenAsks = 10;

    /// <summary>A game was finished (summary shown): asks for a review when it is time.</summary>
    public async Task GameCompletedAsync()
    {
        var games = preferences.Get(GamesKey, 0) + 1;
        preferences.Set(GamesKey, games);
        var last = preferences.Get(LastAskKey, 0);
        var due = last == 0 ? games >= FirstAskAfterGames : games - last >= GamesBetweenAsks;
        if (!due || Platform.CurrentActivity is not { } activity) return;
        preferences.Set(LastAskKey, games);
        try
        {
            var manager = ReviewManagerFactory.Create(activity);
            var info = await AsTask(manager.RequestReviewFlow());
            if (info is null) return;
            await AsTask(manager.LaunchReviewFlow(activity, (ReviewInfo)info));
        }
        catch (Exception e)
        {
            Console.WriteLine($"[BTG] review: {e.Message}");
        }
    }

    /// <summary>Opens the app's page in Google Play (Settings → Oceń aplikację).</summary>
    public static async Task OpenStorePageAsync()
    {
        var id = AppInfo.Current.PackageName;
        if (!await Launcher.TryOpenAsync($"market://details?id={id}"))
            await Launcher.OpenAsync($"https://play.google.com/store/apps/details?id={id}");
    }

    private static Task<Java.Lang.Object?> AsTask(Android.Gms.Tasks.Task task)
    {
        var done = new TaskCompletionSource<Java.Lang.Object?>();
        task.AddOnCompleteListener(new Completed(done));
        return done.Task;
    }

    private sealed class Completed(TaskCompletionSource<Java.Lang.Object?> done) : Java.Lang.Object, Android.Gms.Tasks.IOnCompleteListener
    {
        public void OnComplete(Android.Gms.Tasks.Task task)
        {
            if (task.IsSuccessful) done.TrySetResult(task.Result);
            else done.TrySetException((System.Exception?)task.Exception ?? new InvalidOperationException("Review flow failed."));
        }
    }
}
