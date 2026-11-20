using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Configuration;

namespace TempooAnalytics.Api.Infrastructure.Services;

public class R2StorageService : IStorageService
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;

    public R2StorageService(IConfiguration configuration)
    {
        var accessKey = configuration["Storage:R2:AccessKey"];
        var secretKey = configuration["Storage:R2:SecretKey"];
        var endpoint = configuration["Storage:R2:Endpoint"];
        _bucketName = configuration["Storage:R2:BucketName"] ?? "tempoo-exports";

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(endpoint))
        {
            if (!R2Settings.AllowsLocalFallback(configuration))
            {
                throw new ArgumentNullException("R2 credentials missing in production");
            }
            accessKey = "minioadmin";
            secretKey = "minioadmin";
            endpoint = "http://localhost:9000";
        }

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true // Required for compatibility with generic R2/S3
        };

        _s3Client = new AmazonS3Client(accessKey, secretKey, config);
    }

    private async Task EnsureBucketExistsAsync()
    {
        try
        {
            var response = await _s3Client.ListBucketsAsync();
            if (!response.Buckets.Any(b => b.BucketName == _bucketName))
            {
                await _s3Client.PutBucketAsync(new PutBucketRequest { BucketName = _bucketName, UseClientRegion = true });
            }
        }
        catch (Exception)
        {
            // Ignore errors if it already exists or there are no permissions
        }
    }

    public async Task<string> UploadFileAsync(string key, Stream content, string contentType)
    {
        await EnsureBucketExistsAsync();
        var fileTransferUtility = new TransferUtility(_s3Client);
        
        var uploadRequest = new TransferUtilityUploadRequest
        {
            InputStream = content,
            Key = key,
            BucketName = _bucketName,
            ContentType = contentType
        };

        await fileTransferUtility.UploadAsync(uploadRequest);
        return key;
    }

    public async Task<string> GenerateDownloadUrlAsync(string key, TimeSpan expiration)
    {
        await EnsureBucketExistsAsync();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _bucketName,
            Key = key,
            Expires = DateTime.UtcNow.Add(expiration)
        };

        return _s3Client.GetPreSignedURL(request);
    }

    public async Task DeleteFileAsync(string key)
    {
        await _s3Client.DeleteObjectAsync(_bucketName, key);
    }
}
