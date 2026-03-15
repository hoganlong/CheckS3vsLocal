# CheckS3vsLocal

A .NET 10.0 command-line tool that compares a local directory to an S3 bucket prefix and reports which files are missing from S3. Optionally uploads the missing files.

Useful any time you need to keep a local folder in sync with an S3 bucket — image pipelines, backups, build artifacts, etc.

---

## Usage

```
dotnet run                                        # use appsettings.json defaults, list only
dotnet run -- --upload                            # use appsettings.json defaults + upload missing
dotnet run -- <localpath> <s3uri>                 # override paths, list only
dotnet run -- <localpath> <s3uri> --upload        # override paths + upload missing
```

### Examples

```bash
# See what's missing
dotnet run -- C:\images\artwork s3://my-bucket/artwork/

# Upload missing files
dotnet run -- C:\images\artwork s3://my-bucket/artwork/ --upload
```

---

## Output

```
╔════════════════════════════════════════════════════════════╗
║              S3 vs Local File Checker                      ║
╚════════════════════════════════════════════════════════════╝

Local path : C:\images\artwork
S3 URI     : s3://my-bucket/artwork/
Mode       : list + upload missing

Reading local files...
  142 local files found

Reading S3 objects at s3://my-bucket/artwork/ ...
  139 S3 objects found

═══ Results ═══════════════════════════════════════════════
  In sync          : 139
  Missing from S3  : 3
  Missing locally  : 0 (informational)

Files missing from S3:
  - artwork_A045_full.jpg
  - artwork_A046_full.jpg
  - artwork_A047_full.jpg

Uploading 3 missing file(s)...
  ✓ Uploaded: artwork_A045_full.jpg
  ✓ Uploaded: artwork_A046_full.jpg
  ✓ Uploaded: artwork_A047_full.jpg

Upload complete: 3 succeeded, 0 failed
```

---

## Setup

### Prerequisites

- .NET 10 SDK
- AWS credentials configured (via `aws configure`, environment variables, or IAM role)
- The AWS identity must have `s3:ListBucket` permission, and `s3:PutObject` if using `--upload`

### Install

```bash
git clone https://github.com/hoganlong/CheckS3vsLocal
cd CheckS3vsLocal
cp appsettings.template.json appsettings.json
```

Edit `appsettings.json` with your defaults:

```json
{
  "S3": {
    "LocalPath": "C:\\path\\to\\local\\images",
    "S3Uri": "s3://your-bucket-name/prefix/",
    "Region": "us-east-1"
  }
}
```

Then run:

```bash
dotnet run
```

`appsettings.json` is gitignored so your paths won't be committed. You can always override them with command-line arguments instead.

---

## How It Works

1. Lists all files in the local directory (non-recursive)
2. Lists all S3 objects under the given prefix using paginated `ListObjectsV2`
3. Compares by filename (case-insensitive)
4. Reports files missing from S3 and files in S3 but not local
5. If `--upload` is passed, uploads each missing file using `PutObject`

Already-present files are never re-uploaded. Safe to run repeatedly.

---

## Notes

- Comparison is by filename only — file contents are not checked
- The tool is non-recursive: only the top level of the local directory is compared
- Files in S3 but not local are reported as informational only; the tool never deletes anything
