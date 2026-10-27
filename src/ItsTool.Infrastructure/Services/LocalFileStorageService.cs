using ItsTool.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ItsTool.Infrastructure.Services;

public class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;

    public LocalFileStorageService(IConfiguration configuration)
    {
        _basePath = configuration["FileStorage:BasePath"] ?? "uploads";
        try
        {
            if (!Directory.Exists(_basePath))
            {
                Directory.CreateDirectory(_basePath);
            }
        }
        catch
        {
            // Directory creation will be retried on write operations if initial creation fails
        }
    }

    public async Task<string> SaveFileAsync(IFormFile file, int ticketId)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (UploadPolicy.ContentTypeFor(ext) == null)
            throw new InvalidOperationException($"File type '{ext}' not allowed.");

        if (file.Length > UploadPolicy.MaxBytes)
            throw new InvalidOperationException("File size exceeds 10MB limit.");

        if (UploadPolicy.HasSignature(ext))
        {
            var header = new byte[UploadPolicy.HeaderLength];
            int read;
            await using (var input = file.OpenReadStream())
            {
                read = await input.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false);
            }
            if (!UploadPolicy.HasExpectedSignature(ext, header.AsSpan(0, read)))
                throw new InvalidOperationException($"The file content is not a valid '{ext}' file.");
        }

        var ticketFolder = Path.Combine(_basePath, ticketId.ToString());
        if (!Directory.Exists(ticketFolder))
            Directory.CreateDirectory(ticketFolder);

        // The client's file name never reaches the file system: a name like x\..\..\a.txt would escape the folder
        var filePath = Path.Combine(ticketFolder, $"{Guid.NewGuid():N}{ext}");

        using (var stream = new FileStream(filePath, FileMode.CreateNew))
        {
            await file.CopyToAsync(stream);
        }

        return filePath;
    }

    public Task DeleteFileAsync(string filePath)
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }
}
