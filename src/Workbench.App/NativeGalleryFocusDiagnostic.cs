using System.Text.Json;

namespace Workbench.App;

// Closed scalar schema only. UI collection stays in the gallery's existing partials.
internal enum NativeGalleryFocusPhase
{
    RequiredInput = 0, InitialOwner = 1, PositionLive = 2, PostPosition = 3,
    PreRender = 4, PostRender = 5, PostPixels = 6, PostEncoder = 7,
    PostFlush = 8, PostDecoder = 9, PostRead = 10, RestoreLive = 11,
    PostRestore = 12, PrePublish = 13
}

internal enum NativeGalleryFocusSite
{
    RequiredNull = 0, InitialOwnerNull = 1, FocusAttachedHelperNull = 2, FocusAttachedOwnerChanged = 3
}

internal enum NativeGalleryFocusCheck
{
    NotCalled = 0, RootUnloaded = 1, PageUnloaded = 2, FocusUnloaded = 3,
    RootXamlRootNull = 4, PageXamlRootMismatch = 5, FocusXamlRootMismatch = 6,
    AncestorHidden = 7, RootReachedPageMissing = 8, RootReachedOwnerMissing = 9,
    OwnerUnloaded = 10, OwnerXamlRootMismatch = 11, RootNotReached = 12,
    OwnerReturned = 13, Unavailable = 14
}

internal enum NativeGalleryAboveRootResult { NotSampled = 0, Found = 1, ChainEnded = 2, Capped = 3, Unavailable = 4 }

internal sealed record NativeGalleryFocusObservation(NativeGalleryFocusPhase Phase, NativeGalleryFocusSite Site,
    NativeGalleryFocusCheck Check, bool? SawPage, bool? SawRoot, int PageDepth, int RootDepth,
    int NearestOwnerDepth, int HiddenDepth, bool DepthCapped);

internal sealed record NativeGalleryFocusDiagnosticData(string SourceCommit, int ProcessId,
    long ProcessCreationFileTimeUtc, bool FocusProvided, bool InitialOwnerFound, bool PositionEntered,
    bool PositionReturned, NativeGalleryFocusObservation? FirstFailure, NativeGalleryFocusObservation? TerminalFailure,
    NativeGalleryAboveRootResult AboveRootResult, int AboveRootVisitedCount, int? AboveRootOwnerDistance)
{
    public int SchemaVersion => 1;
    public string Kind => "gallery-focus-boundary";
    public string ShotId => "upcam-preview";
    public string ProbePhase => "business-seed";
}

internal static class NativeGalleryFocusDiagnostic
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 8 };

    internal static bool TryWrite(RunOptions options, string shotId, string pageKey, string? pluginId,
        string? viewId, string status, string reasonCode, NativeGalleryFocusDiagnosticData data)
    {
        try
        {
            if (options is null || !options.IsIsolated || options.BusinessProbe != "seed"
                || shotId != "upcam-preview" || pageKey != "view" || pluginId != "com.upcam.task-digest"
                || viewId != "digest" || status != "blocked" || reasonCode != "focus-detached"
                || options.ReportPath is null || !Valid(data)) return false;
            var directory = Path.GetDirectoryName(options.ReportPath);
            if (string.IsNullOrEmpty(directory)) return false;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
            if (bytes.Length > 8192) return false;
            Directory.CreateDirectory(directory);
            using var output = new FileStream(Path.Combine(directory, "upcam-preview.focus-diagnostic.json"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(bytes);
            return true;
        }
        catch (Exception) { return false; } // Diagnostics never change the primary probe or receipt.
    }

    private static bool Valid(NativeGalleryFocusDiagnosticData? data)
    {
        if (data is null || data.SourceCommit is not { Length: 40 }
            || data.SourceCommit.Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))
            || data.ProcessId <= 0 || data.ProcessCreationFileTimeUtc is <= 0 or > 2650467743999999999L
            || data.TerminalFailure is null || !ValidObservation(data.TerminalFailure)
            || (data.FirstFailure is not null && !ValidObservation(data.FirstFailure))
            || data.TerminalFailure.Phase is NativeGalleryFocusPhase.PositionLive or NativeGalleryFocusPhase.RestoreLive
            || !Enum.IsDefined(data.AboveRootResult) || data.AboveRootVisitedCount is < 0 or > 16) return false;

        if (data.AboveRootResult == NativeGalleryAboveRootResult.NotSampled)
            return data.AboveRootVisitedCount == 0 && data.AboveRootOwnerDistance is null;
        if (data.TerminalFailure is not { Check: NativeGalleryFocusCheck.RootReachedOwnerMissing, SawPage: true, SawRoot: true })
            return false;
        return data.AboveRootResult switch
        {
            NativeGalleryAboveRootResult.Found => data.AboveRootOwnerDistance is >= 1 and <= 16
                && data.AboveRootOwnerDistance == data.AboveRootVisitedCount,
            NativeGalleryAboveRootResult.ChainEnded => data.AboveRootVisitedCount < 16 && data.AboveRootOwnerDistance is null,
            NativeGalleryAboveRootResult.Capped => data.AboveRootVisitedCount == 16 && data.AboveRootOwnerDistance is null,
            NativeGalleryAboveRootResult.Unavailable => data.AboveRootOwnerDistance is null,
            _ => false
        };
    }

    private static bool ValidObservation(NativeGalleryFocusObservation value)
    {
        if (!Enum.IsDefined(value.Phase) || !Enum.IsDefined(value.Site) || !Enum.IsDefined(value.Check)
            || !Depth(value.PageDepth) || !Depth(value.RootDepth) || !Depth(value.NearestOwnerDepth)
            || !Depth(value.HiddenDepth)) return false;
        return value.Site switch
        {
            NativeGalleryFocusSite.RequiredNull => value.Phase == NativeGalleryFocusPhase.RequiredInput
                && value.Check == NativeGalleryFocusCheck.NotCalled && value.SawPage is null && value.SawRoot is null
                && value.PageDepth == -1 && value.RootDepth == -1 && value.NearestOwnerDepth == -1
                && value.HiddenDepth == -1 && !value.DepthCapped,
            NativeGalleryFocusSite.InitialOwnerNull => value.Phase == NativeGalleryFocusPhase.InitialOwner
                && value.Check is not (NativeGalleryFocusCheck.NotCalled or NativeGalleryFocusCheck.OwnerReturned),
            NativeGalleryFocusSite.FocusAttachedHelperNull => value.Phase is not (NativeGalleryFocusPhase.RequiredInput or NativeGalleryFocusPhase.InitialOwner)
                && value.Check is not (NativeGalleryFocusCheck.NotCalled or NativeGalleryFocusCheck.OwnerReturned),
            NativeGalleryFocusSite.FocusAttachedOwnerChanged => value.Phase is not (NativeGalleryFocusPhase.RequiredInput or NativeGalleryFocusPhase.InitialOwner)
                && value.Check is NativeGalleryFocusCheck.OwnerReturned or NativeGalleryFocusCheck.Unavailable,
            _ => false
        };
    }

    private static bool Depth(int value) => value is >= -1 and <= 255;
}
