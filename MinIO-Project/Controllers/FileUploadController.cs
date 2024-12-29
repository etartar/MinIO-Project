using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;
using MinIO_Project.Models;
using System.IO;
using System.Security.AccessControl;

namespace MinIO_Project.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class FileUploadController : ControllerBase
{
    private readonly ILogger<FileUploadController> _logger;
    private MinioOptions _minioOpitons;

    public FileUploadController(ILogger<FileUploadController> logger, IOptionsMonitor<MinioOptions> options)
    {
        _logger = logger;
        _minioOpitons = options.CurrentValue;

        options.OnChange((options, _) =>
        {
            _minioOpitons = options;
        });
    }

    [HttpPost]
    public async Task<IActionResult> UploadFile([FromForm] FileUploadModel uploadFile)
    {
        try
        {
            var minioClient = new MinioClient()
                .WithEndpoint(_minioOpitons.EndPoint)
                .WithCredentials(_minioOpitons.AccessKey, _minioOpitons.SecretKey)
                .Build();

            string bucketName = _minioOpitons.BucketName;
            string objectName = Guid.NewGuid().ToString().Substring(0, 7) + Path.GetExtension(uploadFile.File.FileName);
            string contentType = uploadFile.File.ContentType;

            BucketExistsArgs bucketExistsArgs = new BucketExistsArgs()
                    .WithBucket(bucketName);

            bool isFound = await minioClient.BucketExistsAsync(bucketExistsArgs).ConfigureAwait(false);

            if (!isFound)
            {
                MakeBucketArgs makeBucketArgs = new MakeBucketArgs()
                    .WithBucket(bucketName);

                await minioClient.MakeBucketAsync(makeBucketArgs).ConfigureAwait(false);
            }

            using (var fileStream = new MemoryStream())
            {
                await uploadFile.File.CopyToAsync(fileStream);
                fileStream.Position = 0;

                PutObjectArgs putObjectArgs = new PutObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(objectName)
                    .WithStreamData(fileStream)
                    .WithObjectSize(fileStream.Length)
                    .WithContentType(contentType);

                await minioClient.PutObjectAsync(putObjectArgs).ConfigureAwait(false);
            }

            return Ok(objectName);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> DownloadFile(string fileName)
    {
        MemoryStream memoryStream = new MemoryStream();

        try
        {
            var minioClient = new MinioClient()
                .WithEndpoint(_minioOpitons.EndPoint)
                .WithCredentials(_minioOpitons.AccessKey, _minioOpitons.SecretKey)
                .Build();

            string bucketName = _minioOpitons.BucketName;

            StatObjectArgs statObjectArgs = new StatObjectArgs()
                    .WithBucket(bucketName)
                    .WithObject(fileName);

            await minioClient.StatObjectAsync(statObjectArgs).ConfigureAwait(false);

            GetObjectArgs getObjectArgs = new GetObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName)
                .WithCallbackStream((stream) =>
                {
                    stream.CopyTo(memoryStream);
                    memoryStream.Seek(0, SeekOrigin.Begin);
                });

            await minioClient.GetObjectAsync(getObjectArgs).ConfigureAwait(false);

            return File(memoryStream, "application/octet-stream", fileName);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
