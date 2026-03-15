using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

class Program
{
  static async Task Main(string[] args)
  {
    var configuration = new ConfigurationBuilder()
      .SetBasePath(Directory.GetCurrentDirectory())
      .AddJsonFile("appsettings.json")
      .Build();

    // Defaults from config
    var localPath = configuration["S3:LocalPath"] ?? "";
    var s3Uri = configuration["S3:S3Uri"] ?? "";
    var region = configuration["S3:Region"] ?? "us-east-1";

    // Parse args: positional args override localPath and s3Uri; --upload is a flag
    bool upload = false;
    var positional = new List<string>();
    foreach (var arg in args)
    {
      if (arg == "--upload")
        upload = true;
      else
        positional.Add(arg);
    }
    if (positional.Count >= 1) localPath = positional[0];
    if (positional.Count >= 2) s3Uri = positional[1];

    Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║              S3 vs Local File Checker                      ║");
    Console.WriteLine("╚════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine($"Local path : {localPath}");
    Console.WriteLine($"S3 URI     : {s3Uri}");
    Console.WriteLine($"Mode       : {(upload ? "list + upload missing" : "list only")}");
    Console.WriteLine();

    // Validate local path
    if (!Directory.Exists(localPath))
    {
      Console.WriteLine($"✗ Local path does not exist: {localPath}");
      return;
    }

    // Parse S3 URI → bucket + prefix
    if (!s3Uri.StartsWith("s3://"))
    {
      Console.WriteLine($"✗ S3 URI must start with s3://  Got: {s3Uri}");
      return;
    }
    var withoutScheme = s3Uri.Substring(5); // strip "s3://"
    var slashIndex = withoutScheme.IndexOf('/');
    string bucket, prefix;
    if (slashIndex < 0)
    {
      bucket = withoutScheme;
      prefix = "";
    }
    else
    {
      bucket = withoutScheme.Substring(0, slashIndex);
      prefix = withoutScheme.Substring(slashIndex + 1);
    }

    try
    {
      var s3Client = new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(region));

      // --- List local files ---
      Console.WriteLine("Reading local files...");
      var localFiles = Directory.GetFiles(localPath)
        .Select(f => Path.GetFileName(f))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      Console.WriteLine($"  {localFiles.Count} local files found");
      Console.WriteLine();

      // --- List S3 files at prefix ---
      Console.WriteLine($"Reading S3 objects at s3://{bucket}/{prefix} ...");
      var s3Files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
      var request = new ListObjectsV2Request
      {
        BucketName = bucket,
        Prefix = prefix
      };

      ListObjectsV2Response response;
      do
      {
        response = await s3Client.ListObjectsV2Async(request);
        foreach (var obj in response.S3Objects)
        {
          // Strip the prefix to get just the filename portion
          var key = obj.Key;
          if (key.StartsWith(prefix))
            key = key.Substring(prefix.Length);
          // Skip "directory" entries (keys ending with /)
          if (!string.IsNullOrEmpty(key) && !key.EndsWith("/"))
            s3Files.Add(key);
        }
        request.ContinuationToken = response.NextContinuationToken;
      } while (response.IsTruncated == true);

      Console.WriteLine($"  {s3Files.Count} S3 objects found");
      Console.WriteLine();

      // --- Compare ---
      var missingFromS3 = localFiles.Where(f => !s3Files.Contains(f)).OrderBy(f => f).ToList();
      var missingLocally = s3Files.Where(f => !localFiles.Contains(f)).OrderBy(f => f).ToList();
      var inSync = localFiles.Count(f => s3Files.Contains(f));

      Console.WriteLine("═══ Results ═══════════════════════════════════════════════");
      Console.WriteLine($"  In sync          : {inSync}");
      Console.WriteLine($"  Missing from S3  : {missingFromS3.Count}");
      Console.WriteLine($"  Missing locally  : {missingLocally.Count} (informational)");
      Console.WriteLine();

      if (missingFromS3.Count > 0)
      {
        Console.WriteLine("Files missing from S3:");
        foreach (var f in missingFromS3)
          Console.WriteLine($"  - {f}");
        Console.WriteLine();
      }

      if (missingLocally.Count > 0)
      {
        Console.WriteLine("Files in S3 but not local (informational):");
        foreach (var f in missingLocally)
          Console.WriteLine($"  - {f}");
        Console.WriteLine();
      }

      // --- Upload missing files ---
      if (upload && missingFromS3.Count > 0)
      {
        Console.WriteLine($"Uploading {missingFromS3.Count} missing file(s) to s3://{bucket}/{prefix} ...");
        int uploaded = 0;
        int failed = 0;
        foreach (var filename in missingFromS3)
        {
          var localFilePath = Path.Combine(localPath, filename);
          var s3Key = prefix + filename;
          try
          {
            var putRequest = new PutObjectRequest
            {
              BucketName = bucket,
              Key = s3Key,
              FilePath = localFilePath
            };
            await s3Client.PutObjectAsync(putRequest);
            uploaded++;
            Console.WriteLine($"  ✓ Uploaded: {filename}");
          }
          catch (AmazonS3Exception ex)
          {
            failed++;
            Console.WriteLine($"  ✗ Failed: {filename} — {ex.Message}");
          }
        }
        Console.WriteLine();
        Console.WriteLine($"Upload complete: {uploaded} succeeded, {failed} failed");
      }
      else if (upload && missingFromS3.Count == 0)
      {
        Console.WriteLine("Nothing to upload — all local files are already in S3.");
      }
    }
    catch (AmazonS3Exception ex)
    {
      Console.WriteLine($"✗ AWS S3 Error: {ex.Message}");
      Console.WriteLine($"  Error Code: {ex.ErrorCode}");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"✗ Error: {ex.Message}");
    }
  }
}
