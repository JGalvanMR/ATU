using System;
using System.Text.RegularExpressions;
using ATU.CamaraFria.Models;
using Microsoft.Extensions.Logging;
using ZXing.Net.Maui;

namespace ATU.CamaraFria.Services;

public class ScannerService : IScannerService
{
    private readonly ILogger<ScannerService> _logger;

    public ScannerService(ILogger<ScannerService> logger)
    {
        _logger = logger;
    }

    public LabelScanData ParseScannedCode(string rawCode, BarcodeFormat format)
    {
        try
        {
            var scanData = new LabelScanData
            {
                RawCode = rawCode.Trim(),
                Format = format.ToString(),
                ScannedAt = DateTime.UtcNow
            };

            if (format == BarcodeFormat.QrCode)
            {
                var parsed = TryParseJsonCode(rawCode, scanData);
                if (parsed != null) return parsed;
            }

            var barcodeParsed = TryParseBarcodeCode(rawCode, scanData);
            if (barcodeParsed != null) return barcodeParsed;

            scanData.ExtractedBatchId = rawCode.Trim();
            scanData.ExtractedProduct = null;

            return scanData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error al parsear código escaneado: {Code}", rawCode);
            throw new InvalidOperationException($"Código no válido: {rawCode}");
        }
    }

    public bool IsValidLabelCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        if (code.Trim().Length < 4) return false;

        return Regex.IsMatch(code.Trim(), @"^[A-Za-z0-9\-_.]+$");
    }

    public string FormatBatchIdForDisplay(string batchId)
    {
        if (string.IsNullOrEmpty(batchId)) return "N/A";

        if (batchId.Length > 20)
        {
            return batchId.Substring(0, 17) + "...";
        }

        return batchId.ToUpperInvariant();
    }

    private LabelScanData? TryParseJsonCode(string rawCode, LabelScanData scanData)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(rawCode);
            var root = json.RootElement;

            if (root.TryGetProperty("lote", out var lote))
            {
                scanData.ExtractedBatchId = lote.GetString() ?? rawCode;
            }
            else if (root.TryGetProperty("batchId", out var batchId))
            {
                scanData.ExtractedBatchId = batchId.GetString() ?? rawCode;
            }
            else if (root.TryGetProperty("batch", out var batch))
            {
                scanData.ExtractedBatchId = batch.GetString() ?? rawCode;
            }

            if (root.TryGetProperty("producto", out var producto))
            {
                scanData.ExtractedProduct = producto.GetString();
            }
            else if (root.TryGetProperty("product", out var product))
            {
                scanData.ExtractedProduct = product.GetString();
            }

            return scanData;
        }
        catch
        {
            return null;
        }
    }

    private LabelScanData? TryParseBarcodeCode(string rawCode, LabelScanData scanData)
    {
        var pattern = @"^([A-Z]?-?\d{4}\d{2}\d{2}-?\d+)";
        var match = Regex.Match(rawCode.Trim(), pattern);

        if (match.Success)
        {
            scanData.ExtractedBatchId = match.Groups[1].Value;
            return scanData;
        }

        if (Regex.IsMatch(rawCode.Trim(), @"^\d{6,}$"))
        {
            scanData.ExtractedBatchId = rawCode.Trim();
            return scanData;
        }

        return null;
    }
}

public interface IScannerService
{
    LabelScanData ParseScannedCode(string rawCode, BarcodeFormat format);
    bool IsValidLabelCode(string code);
    string FormatBatchIdForDisplay(string batchId);
}