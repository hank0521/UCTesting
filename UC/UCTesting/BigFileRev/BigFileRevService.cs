using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BigFileReverseService
{
    #region 介面定義

    /// <summary>
    /// 檔案反轉演算法統一介面
    /// 定義所有反轉演算法必須實作的方法和屬性
    /// </summary>
    public interface IFileReverseAlgorithm
    {
        /// <summary>
        /// 演算法名稱
        /// </summary>
        string AlgorithmName { get; }

        /// <summary>
        /// 演算法描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 推薦的檔案大小範圍
        /// </summary>
        (long minSize, long maxSize) RecommendedSizeRange { get; }

        /// <summary>
        /// 檢查是否適合處理指定大小的檔案
        /// </summary>
        /// <param name="fileSize">檔案大小</param>
        /// <param name="availableMemoryMB">可用記憶體(MB)</param>
        /// <returns>是否適合</returns>
        bool IsSuitableFor(long fileSize, long availableMemoryMB);

        /// <summary>
        /// 估算資源需求
        /// </summary>
        /// <param name="fileSize">檔案大小</param>
        /// <returns>(記憶體需求MB, 磁碟空間需求MB, 預估處理時間秒)</returns>
        (long memoryMB, long diskSpaceMB, double estimatedSeconds) EstimateResources(long fileSize);

        /// <summary>
        /// 執行檔案反轉
        /// </summary>
        /// <param name="filePath">檔案路徑</param>
        /// <param name="encoding">檔案編碼</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>處理結果</returns>
        Task<ReverseResult> ReverseFileAsync(string filePath, FileEncoding encoding = FileEncoding.Auto, CancellationToken cancellationToken = default);

        /// <summary>
        /// 進度變更事件
        /// </summary>
        event EventHandler<ProgressEventArgs> ProgressChanged;
    }

    #endregion

    #region 共用類型定義

    /// <summary>
    /// 檔案編碼類型
    /// </summary>
    public enum FileEncoding
    {
        Auto,
        UTF8,
        Big5
    }

    /// <summary>
    /// 處理結果
    /// </summary>
    public class ReverseResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan ProcessTime { get; set; }
        public long FileSize { get; set; }
        public FileEncoding DetectedEncoding { get; set; }
        public string AlgorithmUsed { get; set; }
        public long PeakMemoryUsed { get; set; }
        public long TempDiskSpaceUsed { get; set; }
        public int IOOperations { get; set; }
        public double ProcessingSpeed { get; set; }
        public int EncodingBoundaryAdjustments { get; set; }
        public Dictionary<string, object> AdditionalMetrics { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// 進度事件參數
    /// </summary>
    public class ProgressEventArgs : EventArgs
    {
        public int ProgressPercentage { get; set; }
        public string CurrentPhase { get; set; }
        public string DetailMessage { get; set; }
        public long ProcessedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double CurrentSpeed { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedRemaining { get; set; }
    }

    #endregion

    #region 抽象BASE類

    /// <summary>
    /// 檔案反轉演算法抽象基類
    /// 提供通用功能實作，減少重複代碼
    /// </summary>
    public abstract class FileReverseAlgorithmBase : IFileReverseAlgorithm
    {
        protected DateTime _operationStartTime;
        protected FileEncoding _detectedEncoding;

        public abstract string AlgorithmName { get; }
        public abstract string Description { get; }
        public abstract (long minSize, long maxSize) RecommendedSizeRange { get; }

        public event EventHandler<ProgressEventArgs> ProgressChanged;

        public abstract bool IsSuitableFor(long fileSize, long availableMemoryMB);
        public abstract (long memoryMB, long diskSpaceMB, double estimatedSeconds) EstimateResources(long fileSize);
        public abstract Task<ReverseResult> ReverseFileAsync(string filePath, FileEncoding encoding = FileEncoding.Auto, CancellationToken cancellationToken = default);

        #region 通用輔助方法

        /// <summary>
        /// 驗證檔案
        /// </summary>
        protected virtual string ValidateFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return "檔案路徑不能為空";

            if (!File.Exists(filePath))
                return $"檔案不存在: {filePath}";

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                    return "檔案大小為0，無需處理";

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    // 檢查讀寫權限
                }
            }
            catch (UnauthorizedAccessException)
            {
                return "檔案無法存取，請檢查權限";
            }
            catch (IOException ex)
            {
                return $"檔案I/O錯誤: {ex.Message}";
            }

            return null;
        }

        /// <summary>
        /// 檢測檔案編碼
        /// </summary>
        protected virtual FileEncoding DetectFileEncoding(string filePath)
        {
            byte[] sample = new byte[1024];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                int bytesRead = fs.Read(sample, 0, sample.Length);

                // UTF-8 BOM檢測
                if (bytesRead >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF)
                {
                    return FileEncoding.UTF8;
                }

                // 簡單的編碼推斷
                int highByteCount = 0;
                for (int i = 0; i < bytesRead; i++)
                {
                    if (sample[i] > 127) highByteCount++;
                }

                return highByteCount > bytesRead * 0.3 ? FileEncoding.UTF8 : FileEncoding.UTF8;
            }
        }

        /// <summary>
        /// 調整UTF-8字符邊界
        /// </summary>
        protected int AdjustForUTF8Boundary(byte[] buffer, int size)
        {
            if (size <= 1) return size;

            for (int i = size - 1; i >= Math.Max(0, size - 4); i--)
            {
                byte b = buffer[i];

                if ((b & 0x80) == 0)
                {
                    return i + 1; // ASCII字符邊界
                }

                if ((b & 0xC0) == 0xC0)
                {
                    int charBytes = 0;
                    if ((b & 0xE0) == 0xC0) charBytes = 2;
                    else if ((b & 0xF0) == 0xE0) charBytes = 3;
                    else if ((b & 0xF8) == 0xF0) charBytes = 4;

                    if (i + charBytes <= size)
                    {
                        return i + charBytes;
                    }
                    else
                    {
                        return i;
                    }
                }
            }

            return size;
        }

        /// <summary>
        /// 調整Big5字符邊界
        /// </summary>
        protected int AdjustForBig5Boundary(byte[] buffer, int size)
        {
            if (size <= 1) return size;

            byte lastByte = buffer[size - 1];

            if (lastByte <= 0x7F)
            {
                return size; // ASCII字符
            }

            if (lastByte >= 0xA1 && lastByte <= 0xFE)
            {
                if (size >= 2)
                {
                    byte prevByte = buffer[size - 2];
                    if (prevByte >= 0xA1 && prevByte <= 0xFE)
                    {
                        return size; // 完整的2字節字符
                    }
                }

                return size - 1; // 未完成的字符開始
            }

            return size;
        }

        /// <summary>
        /// 調整編碼邊界
        /// </summary>
        protected int AdjustForEncodingBoundary(byte[] buffer, int size, FileEncoding encoding)
        {
            switch (encoding)
            {
                case FileEncoding.UTF8:
                    return AdjustForUTF8Boundary(buffer, size);
                case FileEncoding.Big5:
                    return AdjustForBig5Boundary(buffer, size);
                default:
                    return size;
            }
        }

        /// <summary>
        /// 完整讀取指定大小的數據
        /// </summary>
        protected async Task<int> ReadFullBufferAsync(Stream stream, byte[] buffer, int count, CancellationToken cancellationToken)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int bytesRead = await stream.ReadAsync(buffer, totalRead, count - totalRead, cancellationToken);
                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }
            return totalRead;
        }

        /// <summary>
        /// 報告進度
        /// </summary>
        protected void ReportProgress(int percentage, string phase, string detail, long processed, long total, double speed = 0)
        {
            var elapsed = DateTime.Now - _operationStartTime;
            var remaining = speed > 0 && processed > 0 ?
                TimeSpan.FromSeconds(((total - processed) / (1024.0 * 1024.0)) / speed) :
                TimeSpan.Zero;

            ProgressChanged?.Invoke(this, new ProgressEventArgs
            {
                ProgressPercentage = percentage,
                CurrentPhase = phase,
                DetailMessage = detail,
                ProcessedBytes = processed,
                TotalBytes = total,
                CurrentSpeed = speed,
                ElapsedTime = elapsed,
                EstimatedRemaining = remaining
            });
        }

        #endregion
    }

    #endregion

    #region 演算法實作

    /// <summary>
    /// 一次性讀取演算法，讀取整包檔案做反轉。
    /// </summary>
    public class SimpleLoadAlgorithm : FileReverseAlgorithmBase
    {
        public override string AlgorithmName => "一次性讀取法";
        public override string Description => "一次性讀取演算法，讀取整包檔案做反轉。";
        public override (long minSize, long maxSize) RecommendedSizeRange => (0, 100 * 1024 * 1024); // 0-100MB

        public override bool IsSuitableFor(long fileSize, long availableMemoryMB)
        {
            long fileSizeMB = fileSize / (1024 * 1024);
            return fileSize < 2L * 1024 * 1024 * 1024 && availableMemoryMB > fileSizeMB * 1.5; // <2GB且記憶體充足
        }

        public override (long memoryMB, long diskSpaceMB, double estimatedSeconds) EstimateResources(long fileSize)
        {
            long memoryMB = fileSize / (1024 * 1024) + 10; // 檔案大小 + 緩衝
            long diskSpaceMB = 0; // 不需要額外磁碟空間
            double estimatedSeconds = (fileSize / (1024.0 * 1024.0)) * 0.01; // 估算每MB需0.01秒

            return (memoryMB, diskSpaceMB, estimatedSeconds);
        }

        public override async Task<ReverseResult> ReverseFileAsync(string filePath, FileEncoding encoding = FileEncoding.Auto, CancellationToken cancellationToken = default)
        {
            var result = new ReverseResult { AlgorithmUsed = AlgorithmName };
            _operationStartTime = DateTime.Now;

            try
            {
                var validationError = ValidateFile(filePath);
                if (validationError != null)
                {
                    result.Success = false;
                    result.Message = validationError;
                    return result;
                }

                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                result.PeakMemoryUsed = fileInfo.Length;
                result.TempDiskSpaceUsed = 0;

                _detectedEncoding = encoding == FileEncoding.Auto ? DetectFileEncoding(filePath) : encoding;
                result.DetectedEncoding = _detectedEncoding;

                ReportProgress(0, "讀取檔案", "載入整個檔案到記憶體", 0, result.FileSize);

                // 一次性讀取所有資料
                byte[] allBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                result.IOOperations++;

                ReportProgress(50, "反轉資料", "執行位元組陣列反轉", result.FileSize / 2, result.FileSize);

                // 直接反轉
                Array.Reverse(allBytes);

                ReportProgress(75, "寫入檔案", "將反轉後的資料寫回檔案", result.FileSize * 3 / 4, result.FileSize);

                // 寫回檔案
                await File.WriteAllBytesAsync(filePath, allBytes, cancellationToken);
                result.IOOperations++;

                result.ProcessTime = DateTime.Now - _operationStartTime;
                result.Success = true;
                result.Message = "一次性讀取反轉完成";
                result.ProcessingSpeed = (result.FileSize / (1024.0 * 1024.0)) / result.ProcessTime.TotalSeconds;

                ReportProgress(100, "完成", "檔案反轉完成", result.FileSize, result.FileSize, result.ProcessingSpeed);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "操作被取消";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"處理失敗: {ex.Message}";
            }

            return result;
        }
    }

    /// <summary>
    /// Stack順序演算法 - 適用於中等檔案且記憶體充足的情況
    /// </summary>
    public class StackSequentialAlgorithm : FileReverseAlgorithmBase
    {
        private readonly int _chunkSize;

        public StackSequentialAlgorithm(int chunkSize = 1024 * 1024) // 預設1MB
        {
            _chunkSize = chunkSize;
        }

        public override string AlgorithmName => "Stack順序法";
        public override string Description => "順序讀取分塊資料並使用Stack先進後出特性，適合記憶體充足的中等檔案";
        public override (long minSize, long maxSize) RecommendedSizeRange => (10 * 1024 * 1024, 2L * 1024 * 1024 * 1024); // 10MB-2GB

        public override bool IsSuitableFor(long fileSize, long availableMemoryMB)
        {
            long fileSizeMB = fileSize / (1024 * 1024);
            return fileSizeMB >= 10 && availableMemoryMB > fileSizeMB * 1.2; // 記憶體必須充足
        }

        public override (long memoryMB, long diskSpaceMB, double estimatedSeconds) EstimateResources(long fileSize)
        {
            long memoryMB = fileSize / (1024 * 1024) + 50; // 檔案大小 + Stack開銷
            long diskSpaceMB = 0; // 不需要額外磁碟空間
            double estimatedSeconds = (fileSize / (1024.0 * 1024.0)) * 0.05; // 估算每MB需0.05秒

            return (memoryMB, diskSpaceMB, estimatedSeconds);
        }

        public override async Task<ReverseResult> ReverseFileAsync(string filePath, FileEncoding encoding = FileEncoding.Auto, CancellationToken cancellationToken = default)
        {
            var result = new ReverseResult { AlgorithmUsed = AlgorithmName };
            _operationStartTime = DateTime.Now;

            try
            {
                var validationError = ValidateFile(filePath);
                if (validationError != null)
                {
                    result.Success = false;
                    result.Message = validationError;
                    return result;
                }

                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                result.PeakMemoryUsed = fileInfo.Length;
                result.TempDiskSpaceUsed = 0;

                _detectedEncoding = encoding == FileEncoding.Auto ? DetectFileEncoding(filePath) : encoding;
                result.DetectedEncoding = _detectedEncoding;

                var processedData = new Stack<byte[]>();

                // 階段1：順序讀取 + Stack壓入
                ReportProgress(0, "順序讀取", "分塊讀取並放入Stack", 0, result.FileSize);

                using (var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    byte[] chunkBuffer = new byte[_chunkSize];
                    long totalProcessed = 0;

                    while (totalProcessed < result.FileSize && !cancellationToken.IsCancellationRequested)
                    {
                        long remainingBytes = result.FileSize - totalProcessed;
                        int targetSize = (int)Math.Min(_chunkSize, remainingBytes);
                        int actualRead = await ReadFullBufferAsync(reader, chunkBuffer, targetSize, cancellationToken);

                        if (actualRead == 0) break;

                        // 編碼邊界調整
                        int adjustedSize = actualRead;
                        if (totalProcessed + actualRead < result.FileSize)
                        {
                            adjustedSize = AdjustForEncodingBoundary(chunkBuffer, actualRead, _detectedEncoding);
                            if (adjustedSize != actualRead)
                            {
                                reader.Seek(adjustedSize - actualRead, SeekOrigin.Current);
                                result.EncodingBoundaryAdjustments++;
                            }
                        }

                        // 創建chunk副本並反轉
                        byte[] chunk = new byte[adjustedSize];
                        Array.Copy(chunkBuffer, 0, chunk, 0, adjustedSize);
                        Array.Reverse(chunk);

                        // Stack自動反序
                        processedData.Push(chunk);

                        totalProcessed += adjustedSize;
                        result.IOOperations++;

                        int progress = (int)((totalProcessed * 50) / result.FileSize);
                        ReportProgress(progress, "順序讀取", $"已處理 {processedData.Count} 個區塊", totalProcessed, result.FileSize);
                    }
                }

                // 階段2：Stack彈出 + 順序寫入
                ReportProgress(50, "順序寫入", "從Stack彈出並寫入檔案", 0, result.FileSize);

                using (var writer = new FileStream(filePath, FileMode.Create, FileAccess.Write))
                {
                    long totalWritten = 0;

                    while (processedData.Count > 0 && !cancellationToken.IsCancellationRequested)
                    {
                        byte[] chunk = processedData.Pop();
                        await writer.WriteAsync(chunk, 0, chunk.Length, cancellationToken);

                        totalWritten += chunk.Length;
                        result.IOOperations++;

                        int progress = 50 + (int)((totalWritten * 50) / result.FileSize);
                        ReportProgress(progress, "順序寫入", $"剩餘 {processedData.Count} 個區塊", totalWritten, result.FileSize);
                    }

                    await writer.FlushAsync(cancellationToken);
                }

                result.ProcessTime = DateTime.Now - _operationStartTime;
                result.Success = true;
                result.Message = $"Stack順序反轉完成，處理了 {result.IOOperations / 2} 個區塊，調整了 {result.EncodingBoundaryAdjustments} 次字符邊界";
                result.ProcessingSpeed = (result.FileSize / (1024.0 * 1024.0)) / result.ProcessTime.TotalSeconds;

                ReportProgress(100, "完成", "Stack順序反轉完成", result.FileSize, result.FileSize, result.ProcessingSpeed);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "操作被取消";
            }
            catch (OutOfMemoryException)
            {
                result.Success = false;
                result.Message = "記憶體不足，建議使用雙指針法或流式分塊法";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"處理失敗: {ex.Message}";
            }

            return result;
        }
    }

    /// <summary>
    /// 雙指針就地交換演算法 - 固定記憶體使用，適合超大檔案
    /// </summary>
    public class DualPointerAlgorithm : FileReverseAlgorithmBase
    {
        private readonly int _bufferSize;

        public DualPointerAlgorithm(int bufferSize = 64 * 1024) // 預設64KB
        {
            _bufferSize = bufferSize;
        }

        public override string AlgorithmName => "雙指針就地交換法";
        public override string Description => "使用雙指針從檔案兩端向中間交換資料，固定記憶體使用，適合超大檔案";
        public override (long minSize, long maxSize) RecommendedSizeRange => (100 * 1024 * 1024, long.MaxValue); // 100MB+

        public override bool IsSuitableFor(long fileSize, long availableMemoryMB)
        {
            // 任何大小都適用，記憶體需求固定
            return true;
        }

        public override (long memoryMB, long diskSpaceMB, double estimatedSeconds) EstimateResources(long fileSize)
        {
            long memoryMB = (_bufferSize * 2) / (1024 * 1024) + 1; // 固定兩個buffer
            long diskSpaceMB = 0; // 就地交換，不需額外空間
            double estimatedSeconds = (fileSize / (1024.0 * 1024.0)) * 0.08; // 隨機I/O較慢

            return (memoryMB, diskSpaceMB, estimatedSeconds);
        }

        public override async Task<ReverseResult> ReverseFileAsync(string filePath, FileEncoding encoding = FileEncoding.Auto, CancellationToken cancellationToken = default)
        {
            var result = new ReverseResult { AlgorithmUsed = AlgorithmName };
            _operationStartTime = DateTime.Now;

            try
            {
                var validationError = ValidateFile(filePath);
                if (validationError != null)
                {
                    result.Success = false;
                    result.Message = validationError;
                    return result;
                }

                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                result.PeakMemoryUsed = (_bufferSize * 2);
                result.TempDiskSpaceUsed = 0;

                _detectedEncoding = encoding == FileEncoding.Auto ? DetectFileEncoding(filePath) : encoding;
                result.DetectedEncoding = _detectedEncoding;

                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite))
                {
                    byte[] leftBuffer = new byte[_bufferSize];
                    byte[] rightBuffer = new byte[_bufferSize];
                    long fileSize = fs.Length;
                    long leftPos = 0;
                    long rightPos = fileSize;
                    long totalProcessed = 0;

                    ReportProgress(0, "雙指針交換", "從檔案兩端開始交換", 0, fileSize);

                    while (leftPos < rightPos && !cancellationToken.IsCancellationRequested)
                    {
                        // 計算讀取大小，避免重疊
                        long remainingBytes = rightPos - leftPos;
                        int readSize = (int)Math.Min(_bufferSize, remainingBytes / 2);
                        if (readSize <= 0) break;

                        rightPos -= readSize;

                        // 讀取左邊區塊
                        fs.Seek(leftPos, SeekOrigin.Begin);
                        int leftRead = await fs.ReadAsync(leftBuffer, 0, readSize, cancellationToken);

                        // 讀取右邊區塊
                        fs.Seek(rightPos, SeekOrigin.Begin);
                        int rightRead = await fs.ReadAsync(rightBuffer, 0, readSize, cancellationToken);

                        // 處理編碼邊界調整
                        if (_detectedEncoding != FileEncoding.Auto)
                        {
                            leftRead = AdjustForEncodingBoundary(leftBuffer, leftRead, _detectedEncoding);
                            rightRead = AdjustForEncodingBoundary(rightBuffer, rightRead, _detectedEncoding);
                            result.EncodingBoundaryAdjustments += 2;
                        }

                        // 反轉各自內容
                        Array.Reverse(leftBuffer, 0, leftRead);
                        Array.Reverse(rightBuffer, 0, rightRead);

                        // 交換寫入
                        fs.Seek(leftPos, SeekOrigin.Begin);
                        await fs.WriteAsync(rightBuffer, 0, rightRead, cancellationToken);

                        fs.Seek(rightPos, SeekOrigin.Begin);
                        await fs.WriteAsync(leftBuffer, 0, leftRead, cancellationToken);

                        leftPos += leftRead;
                        totalProcessed += leftRead + rightRead;
                        result.IOOperations += 4; // 2讀 + 2寫

                        int progress = (int)((totalProcessed * 100) / fileSize);
                        ReportProgress(progress, "雙指針交換", $"已處理 {totalProcessed / (1024.0 * 1024.0):F1}MB", totalProcessed, fileSize);
                    }

                    // 處理中間剩餘的單個區塊
                    if (leftPos == rightPos && leftPos < fileSize)
                    {
                        fs.Seek(leftPos, SeekOrigin.Begin);
                        int remaining = (int)(fileSize - leftPos);
                        int finalRead = await fs.ReadAsync(leftBuffer, 0, Math.Min(remaining, _bufferSize), cancellationToken);

                        Array.Reverse(leftBuffer, 0, finalRead);

                        fs.Seek(leftPos, SeekOrigin.Begin);
                        await fs.WriteAsync(leftBuffer, 0, finalRead, cancellationToken);
                        result.IOOperations += 2;
                    }

                    await fs.FlushAsync(cancellationToken);
                }

                result.ProcessTime = DateTime.Now - _operationStartTime;
                result.Success = true;
                result.Message = $"雙指針就地交換完成，調整了 {result.EncodingBoundaryAdjustments} 次字符邊界";
                result.ProcessingSpeed = (result.FileSize / (1024.0 * 1024.0)) / result.ProcessTime.TotalSeconds;

                ReportProgress(100, "完成", "雙指針交換完成", result.FileSize, result.FileSize, result.ProcessingSpeed);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "操作被取消";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"處理失敗: {ex.Message}";
            }

            return result;
        }
    }

    /// <summary>
    /// 流式分塊演算法 - 使用臨時檔案，記憶體使用固定
    /// </summary>
    public class StreamingChunkAlgorithm : FileReverseAlgorithmBase
    {
        private readonly int _chunkSize;

        public StreamingChunkAlgorithm(int chunkSize = 16 * 1024 * 1024) // 預設16MB
        {
            _chunkSize = chunkSize;
        }

        public override string AlgorithmName => "流式分塊法";
        public override string Description => "使用臨時檔案的流式處理，固定記憶體使用，適合磁碟空間充足的大檔案";
        public override (long minSize, long maxSize) RecommendedSizeRange => (100 * 1024 * 1024, long.MaxValue); // 100MB+

        public override bool IsSuitableFor(long fileSize, long availableMemoryMB)
        {
            // 任何大小都適用，只需要固定記憶體
            return true;
        }

        public override (long memoryMB, long diskSpaceMB, double estimatedSeconds) EstimateResources(long fileSize)
        {
            long memoryMB = _chunkSize / (1024 * 1024) + 10; // 固定區塊大小 + 緩衝
            long diskSpaceMB = fileSize / (1024 * 1024); // 需要100%檔案大小的臨時空間
            double estimatedSeconds = (fileSize / (1024.0 * 1024.0)) * 0.06; // 順序I/O較快

            return (memoryMB, diskSpaceMB, estimatedSeconds);
        }

        public override async Task<ReverseResult> ReverseFileAsync(string filePath, FileEncoding encoding = FileEncoding.Auto, CancellationToken cancellationToken = default)
        {
            var result = new ReverseResult { AlgorithmUsed = AlgorithmName };
            _operationStartTime = DateTime.Now;
            string tempFile = filePath + ".tmp_streaming";

            try
            {
                var validationError = ValidateFile(filePath);
                if (validationError != null)
                {
                    result.Success = false;
                    result.Message = validationError;
                    return result;
                }

                var fileInfo = new FileInfo(filePath);
                result.FileSize = fileInfo.Length;
                result.PeakMemoryUsed = _chunkSize;
                result.TempDiskSpaceUsed = fileInfo.Length;

                _detectedEncoding = encoding == FileEncoding.Auto ? DetectFileEncoding(filePath) : encoding;
                result.DetectedEncoding = _detectedEncoding;

                // 階段1：順序讀取，反向寫入臨時檔案
                ReportProgress(0, "流式分塊處理", "順序讀取並反向寫入臨時檔案", 0, result.FileSize);

                using (var reader = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                using (var writer = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                {
                    var fileSize = reader.Length;
                    writer.SetLength(fileSize);

                    byte[] buffer = new byte[_chunkSize];
                    long position = 0;

                    while (position < fileSize && !cancellationToken.IsCancellationRequested)
                    {
                        // 順序讀取
                        long remainingBytes = fileSize - position;
                        int targetSize = (int)Math.Min(_chunkSize, remainingBytes);
                        int bytesRead = await ReadFullBufferAsync(reader, buffer, targetSize, cancellationToken);

                        if (bytesRead == 0) break;

                        // 編碼邊界調整
                        int adjustedSize = bytesRead;
                        if (position + bytesRead < fileSize)
                        {
                            adjustedSize = AdjustForEncodingBoundary(buffer, bytesRead, _detectedEncoding);
                            if (adjustedSize != bytesRead)
                            {
                                reader.Seek(adjustedSize - bytesRead, SeekOrigin.Current);
                                result.EncodingBoundaryAdjustments++;
                            }
                        }

                        // 反轉這個區塊
                        Array.Reverse(buffer, 0, adjustedSize);

                        // 計算在臨時檔案中的反向位置
                        long writePosition = fileSize - position - adjustedSize;
                        writer.Seek(writePosition, SeekOrigin.Begin);
                        await writer.WriteAsync(buffer, 0, adjustedSize, cancellationToken);

                        position += adjustedSize;
                        result.IOOperations += 2; // 1讀 + 1寫

                        int progress = (int)((position * 50) / fileSize);
                        ReportProgress(progress, "流式分塊處理", $"已處理 {position / (1024.0 * 1024.0):F1}MB", position, fileSize);
                    }

                    await writer.FlushAsync(cancellationToken);
                }

                // 階段2：替換原檔案
                ReportProgress(75, "替換檔案", "替換原檔案", result.FileSize * 3 / 4, result.FileSize);

                File.Delete(filePath);
                File.Move(tempFile, filePath);
                tempFile = null; // 防止finally中誤刪

                result.ProcessTime = DateTime.Now - _operationStartTime;
                result.Success = true;
                result.Message = $"流式分塊反轉完成，調整了 {result.EncodingBoundaryAdjustments} 次字符邊界";
                result.ProcessingSpeed = (result.FileSize / (1024.0 * 1024.0)) / result.ProcessTime.TotalSeconds;

                ReportProgress(100, "完成", "流式分塊反轉完成", result.FileSize, result.FileSize, result.ProcessingSpeed);
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Message = "操作被取消";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"處理失敗: {ex.Message}";
            }
            finally
            {
                // 清理臨時檔案
                if (!string.IsNullOrEmpty(tempFile) && File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                        // 忽略清理錯誤
                    }
                }
            }

            return result;
        }
    }

    #endregion

    #region 演算法工廠類

    /// <summary>
    /// 檔案反轉演算法工廠
    /// 負責根據檔案特性自動選擇最適合的演算法
    /// </summary>
    public static class FileReverseAlgorithmFactory
    {
        /// <summary>
        /// 根據檔案大小和系統資源自動選擇最佳演算法
        /// </summary>
        /// <param name="fileSize">檔案大小</param>
        /// <param name="availableMemoryMB">可用記憶體(MB)</param>
        /// <param name="availableDiskSpaceMB">可用磁碟空間(MB)</param>
        /// <returns>推薦的演算法實例</returns>
        public static IFileReverseAlgorithm SelectBestAlgorithm(long fileSize, long availableMemoryMB, long availableDiskSpaceMB)
        {
            long fileSizeMB = fileSize / (1024 * 1024);

            // 小檔案且記憶體充足：一次性讀取
            if (fileSize < 2L * 1024 * 1024 * 1024 && availableMemoryMB > fileSizeMB * 1.5)
            {
                return new SimpleLoadAlgorithm();
            }

            // 中檔案且記憶體充足：Stack順序法
            if (fileSizeMB < 5000 && availableMemoryMB > fileSizeMB * 1.2)
            {
                return new StackSequentialAlgorithm();
            }

            // 大檔案且磁碟空間充足：流式分塊法
            if (availableDiskSpaceMB > fileSizeMB)
            {
                return new StreamingChunkAlgorithm();
            }

            // 記憶體和磁碟都受限：雙指針法 (最後選擇)
            return new DualPointerAlgorithm();
        }

        /// <summary>
        /// 獲取所有可用的演算法
        /// </summary>
        /// <returns>演算法清單</returns>
        public static List<IFileReverseAlgorithm> GetAllAlgorithms()
        {
            return new List<IFileReverseAlgorithm>
            {
                new SimpleLoadAlgorithm(),
                new StackSequentialAlgorithm(),
                new DualPointerAlgorithm(),
                new StreamingChunkAlgorithm()
            };
        }

        /// <summary>
        /// 根據演算法名稱獲取演算法實例
        /// </summary>
        /// <param name="algorithmName">演算法名稱</param>
        /// <returns>演算法實例，如果找不到則返回null</returns>
        public static IFileReverseAlgorithm GetAlgorithmByName(string algorithmName)
        {
            switch (algorithmName?.ToLower())
            {
                case "一次性讀取法":
                case "simple":
                case "simpleload":
                    return new SimpleLoadAlgorithm();

                case "stack順序法":
                case "stack":
                case "stacksequential":
                    return new StackSequentialAlgorithm();

                case "雙指針就地交換法":
                case "dualpointer":
                case "雙指針法":
                    return new DualPointerAlgorithm();

                case "流式分塊法":
                case "streaming":
                case "streamingchunk":
                    return new StreamingChunkAlgorithm();

                default:
                    return null;
            }
        }

        /// <summary>
        /// 比較不同演算法的資源需求
        /// </summary>
        /// <param name="fileSize">檔案大小</param>
        /// <param name="availableMemoryMB">可用記憶體MB</param>
        /// <returns>各演算法的資源需求比較</returns>
        public static Dictionary<string, (long memoryMB, long diskSpaceMB, double estimatedSeconds, bool suitable)> CompareAlgorithms(long fileSize, long availableMemoryMB = 8192)
        {
            var algorithms = GetAllAlgorithms();
            var comparison = new Dictionary<string, (long memoryMB, long diskSpaceMB, double estimatedSeconds, bool suitable)>();

            foreach (var algorithm in algorithms)
            {
                var (memoryMB, diskSpaceMB, estimatedSeconds) = algorithm.EstimateResources(fileSize);
                bool suitable = algorithm.IsSuitableFor(fileSize, availableMemoryMB);

                comparison[algorithm.AlgorithmName] = (memoryMB, diskSpaceMB, estimatedSeconds, suitable);
            }

            return comparison;
        }

        /// <summary>
        /// 獲取演算法效能比較報告
        /// </summary>
        /// <param name="fileSize">檔案大小</param>
        /// <param name="availableMemoryMB">可用記憶體</param>
        /// <param name="availableDiskSpaceMB">可用磁碟空間</param>
        /// <returns>詳細比較報告</returns>
        public static string GetPerformanceReport(long fileSize, long availableMemoryMB, long availableDiskSpaceMB)
        {
            var fileSizeMB = fileSize / (1024.0 * 1024.0);
            var comparison = CompareAlgorithms(fileSize, availableMemoryMB);
            var bestAlgorithm = SelectBestAlgorithm(fileSize, availableMemoryMB, availableDiskSpaceMB);

            var report = new StringBuilder();
            report.AppendLine($"檔案大小: {fileSizeMB:F1} MB");
            report.AppendLine($"可用記憶體: {availableMemoryMB} MB");
            report.AppendLine($"可用磁碟空間: {availableDiskSpaceMB} MB");
            report.AppendLine($"推薦演算法: {bestAlgorithm.AlgorithmName}");
            report.AppendLine();
            report.AppendLine("演算法比較:");
            report.AppendLine("演算法名稱".PadRight(15) + "記憶體需求".PadRight(10) + "磁碟需求".PadRight(10) + "預估時間".PadRight(10) + "適用性");
            report.AppendLine(new string('-', 60));

            foreach (var (name, (memoryMB, diskSpaceMB, estimatedSeconds, suitable)) in comparison)
            {
                report.AppendLine($"{name.PadRight(15)}{memoryMB}MB".PadRight(10) +
                                $"{diskSpaceMB}MB".PadRight(10) +
                                $"{estimatedSeconds:F1}s".PadRight(10) +
                                (suitable ? "✓" : "✗"));
            }

            return report.ToString();
        }
    }

    #endregion



   
}