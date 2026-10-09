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
    }

    [Serializable]
    public class TpPackage
    {
        public string slug;
        public string name;
        public string category;
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
        public long totalSize;
        public List<TpVersion> versions = new List<TpVersion>();

        public TpVersion Find(string v)
        {
            if (versions == null) return null;
            foreach (var x in versions) if (x.version == v) return x;
            return null;
        }

        public TpVersion Latest { get { return Find(latest) ?? (versions != null && versions.Count > 0 ? versions[0] : null); } }
        public string DisplayName { get { return string.IsNullOrEmpty(name) ? slug : name; } }
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
        public List<TpPackage> packages = new List<TpPackage>();
    }

    [Serializable]
    public class TpPackageResponse : TpApiBase
    {
        public TpPackage package;
        public bool canEdit;
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
        public string description;
        public string publisher;
        public string[] tags = new string[0];
        public string assetStoreUrl;
    }

    [Serializable]
    public class TpUploadBeginResponse : TpApiBase
    {
        public string uploadId;
        public long chunkSize;
        public int chunks;
        public string slug;
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
        public string description;
        public string publisher;
        public string[] tags = new string[0];
        public string assetStoreUrl;
    }

    [Serializable]
    public class TpSlugVersionRequest
    {
        public string slug;
        public string version;
    }

    [Serializable]
    public class TpConnectStartRequest
    {
        public string label;
    }

    [Serializable]
    public class TpConnectStartResponse : TpApiBase
    {
        public string code;
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
        public List<TpPackage> packages = new List<TpPackage>();
    }
}
