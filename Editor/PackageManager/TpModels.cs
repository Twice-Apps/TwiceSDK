using System;
using System.Collections.Generic;

namespace TwiceSDK.PackageManager
{
    /* Wire format of hub/packages_api.php. JsonUtility needs public fields and
       [Serializable]; missing keys simply stay at their defaults. */

    [Serializable]
    public class TpApiBase
    {
        public bool ok;
        public string error;
        public string code;     // machine-readable error, e.g. "version_exists"
    }

    [Serializable]
    public class TpVersion
    {
        public string version;
        public long size;
        public string sha256;
        public string unity;
        public string changelog;
        public string[] dependencies = new string[0];
        public string[] upmDependencies = new string[0];
        public string[] namespaces = new string[0];
        public string uploadedBy;
        public string uploadedAt;
        public int downloads;
        public string note;         // e.g. "calisan surum bu"
        public string status;       // "" | recommended | broken

        public bool IsBroken { get { return status == "broken"; } }
        public bool IsRecommended { get { return status == "recommended"; } }
    }

    [Serializable]
    public class TpCategoryNode
    {
        public string name;
        public string[] subs = new string[0];
    }

    public static class TpOrigin
    {
        public const string Twice = "twice", Store = "store", Other = "other";
        public static readonly string[] All = { Twice, Store, Other };
        public static readonly string[] Labels = { "Twice", "Asset Store", "Diğer" };
        public static string Label(string o) { int i = Array.IndexOf(All, o); return i >= 0 ? Labels[i] : Labels[2]; }
    }

    [Serializable]
    public class TpPackage
    {
        public string slug;
        public string name;
        public string category;
        public string subcategory;
        public string origin;           // twice / store / other
        public string storeId;
        public bool deprecated;
        public string deprecatedNote;
        public string description;
        public string publisher;
        public string[] tags = new string[0];
        public string assetStoreUrl;
        public string image;
        public string createdBy;
        public string createdAt;
        public string updatedAt;
        public int downloads;
        public string latest;
        public string recommended;      // server-picked default: recommended, else newest not broken
        public string notes;            // package-level notes (admins)
        public long totalSize;
        public List<TpVersion> versions = new List<TpVersion>();

        /// <summary>The version an install should use when nobody picked one.</summary>
        public TpVersion Default { get { return Find(recommended) ?? Latest; } }

        public TpVersion Find(string v)
        {
            if (versions == null) return null;
            foreach (var x in versions) if (x.version == v) return x;
            return null;
        }

        public TpVersion Latest { get { return Find(latest) ?? (versions != null && versions.Count > 0 ? versions[0] : null); } }
        public string DisplayName { get { return string.IsNullOrEmpty(name) ? slug : name; } }
        public bool IsTwice { get { return origin == TpOrigin.Twice; } }
        public string CategoryLabel { get { return string.IsNullOrEmpty(subcategory) ? (category ?? "") : category + " · " + subcategory; } }
    }

    [Serializable]
    public class TpMeResponse : TpApiBase
    {
        public string user;
        public bool super;
        public long chunkSize;
        public long maxBytes;
    }

    [Serializable]
    public class TpCatalogResponse : TpApiBase
    {
        public int rev;
        public string user;
        public bool super;
        public string[] categories = new string[0];
        public List<TpCategoryNode> taxonomy = new List<TpCategoryNode>();
        public List<TpPackage> packages = new List<TpPackage>();
    }

    [Serializable]
    public class TpPackageResponse : TpApiBase
    {
        public TpPackage package;
        public bool canEdit;
        public string[] moved = new string[0];
        public string[] skipped = new string[0];
    }

    [Serializable]
    public class TpUploadBeginRequest
    {
        public string slug;
        public string name;
        public string version;
        public long size;
        public string sha256;
        public string unity;
        public string changelog;
        public string[] dependencies = new string[0];
        public string[] upmDependencies = new string[0];
        public string[] namespaces = new string[0];
        public string category;
        public string subcategory;
        public string origin;
        public string storeId;
        public bool deprecated;
        public string description;
        public string publisher;
        public string[] tags = new string[0];
        public string assetStoreUrl;
        public bool overwrite;
    }

    [Serializable]
    public class TpUploadBeginResponse : TpApiBase
    {
        public string uploadId;
        public long chunkSize;
        public int chunks;
        public string slug;
        public string matchedFrom;
        public bool overwrite;
        public bool newPackage;
    }

    [Serializable]
    public class TpUploadIdRequest
    {
        public string uploadId;
    }

    [Serializable]
    public class TpMetaUpdateRequest
    {
        public string slug;
        public string name;
        public string category;
        public string subcategory;
        public string origin;
        public bool deprecated;
        public string deprecatedNote;
        public string description;
        public string publisher;
        public string[] tags = new string[0];
        public string assetStoreUrl;
        public string notes;
    }

    [Serializable]
    public class TpVersionUpdateRequest
    {
        public string slug;
        public string version;
        public string note;
        public string status;
    }

    [Serializable]
    public class TpSlugVersionRequest
    {
        public string slug;
        public string version;
    }

    [Serializable]
    public class TpMergeRequest
    {
        public string from;
        public string into;
    }

    /* ---- identity (rules + AI) ---------------------------------------------- */

    [Serializable]
    public class TpIdentifyItem
    {
        public string title;
        public string version;
        public string publisher;
        public string category;
        public string storeId;
        public string filename;
        public string origin;
        public string[] namespaces = new string[0];
    }

    [Serializable]
    public class TpIdentifyRequest
    {
        public List<TpIdentifyItem> items = new List<TpIdentifyItem>();
        public bool ai;
    }

    [Serializable]
    public class TpIdentifyMatch
    {
        public string slug;
        public string by;           // store / key / alias / ai
        public float confidence;
    }

    [Serializable]
    public class TpIdentifyCandidate
    {
        public string slug;
        public string name;
        public float score;
    }

    [Serializable]
    public class TpIdentifyResult
    {
        public TpIdentifyMatch match;   // JsonUtility never leaves it null: check match.slug
        public List<TpIdentifyCandidate> candidates = new List<TpIdentifyCandidate>();
        public string name;
        public string category;
        public string subcategory;
        public string publisher;
        public string description;
        public string[] tags = new string[0];
        public string origin;
        public string source;       // rules / cache / ai
        public string note;
        public bool deprecated;

        public string MatchSlug { get { return match != null && !string.IsNullOrEmpty(match.slug) ? match.slug : null; } }
    }

    [Serializable]
    public class TpIdentifyResponse : TpApiBase
    {
        public bool aiAvailable;
        public List<TpIdentifyResult> items = new List<TpIdentifyResult>();
    }

    [Serializable]
    public class TpConnectStartRequest
    {
        public string label;
    }

    [Serializable]
    public class TpConnectStartResponse : TpApiBase
    {
        // the connect code arrives in TpApiBase.code (same JSON key)
        public string pollId;
        public string url;
        public int expiresIn;
    }

    [Serializable]
    public class TpConnectPollRequest
    {
        public string code;
        public string pollId;
    }

    [Serializable]
    public class TpConnectPollResponse : TpApiBase
    {
        public string status;   // pending / approved / expired
        public string token;
        public string user;
    }

    /* ---- local state ------------------------------------------------------ */

    /// <summary>What one package put into this project (ProjectSettings/TwicePackages.json).</summary>
    [Serializable]
    public class TpInstalled
    {
        public string slug;
        public string version;
        public string installedAt;
        public string installedBy;
        public List<string> guids = new List<string>();   // every asset (files + folders) in the .unitypackage
        public List<string> folders = new List<string>(); // guids of folder entries (deleted only when empty)
    }

    [Serializable]
    public class TpInstallState
    {
        public List<TpInstalled> installed = new List<TpInstalled>();
    }

    [Serializable]
    public class TpCatalogCache
    {
        public string hub;
        public int rev;
        public string fetchedAt;
        public string user;
        public bool super;
        public string[] categories = new string[0];
        public List<TpCategoryNode> taxonomy = new List<TpCategoryNode>();
        public List<TpPackage> packages = new List<TpPackage>();
    }
}
