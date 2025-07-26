using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Linq;
using BigFileReverseService;

namespace BigFileReverseTest
{
    class Program
    {
        private const int ValidationSize = 50; // 驗證位元組大小，涵蓋多位元組字符

        static async System.Threading.Tasks.Task Main(string[] args)
        {
            // 註冊編碼提供者以支援 Big5
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            // 定義測試參數
            string testDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BigFileReverseTest");
            string smallFilePath = Path.Combine(testDir, "small_test.txt");
            string largeFilePath = Path.Combine(testDir, "large_test.txt");
            long smallFileSize = 10 * 1024 * 1024; // 10MB
            long largeFileSize = 100 * 1024 * 1024; // 100MB
            long simulateFileSize = 100L * 1024 * 1024 * 1024; // 100GB (僅模擬)
            long availableMemoryMB = 8192; // 假設可用記憶體 8GB
            long availableDiskSpaceMB = 1024; // 假設可用磁碟空間 1GB

            // 測試內容：包含多位元組字符
            string testContent = "測試中文編碼 ABC 測試";

            // 建立測試目錄
            Directory.CreateDirectory(testDir);

            try
            {
                // 建立測試檔案
                CreateTestFile(smallFilePath, smallFileSize, FileEncoding.UTF8, testContent);
                CreateTestFile(largeFilePath, largeFileSize, FileEncoding.Big5, testContent);

                // 模擬 100GB 檔案的推薦範圍檢查（不實際建立）
                Console.WriteLine("=== 模擬 100GB 檔案 ===");
                CheckAndEstimate(simulateFileSize, availableMemoryMB, availableDiskSpaceMB);
                Console.WriteLine();

                // 測試檔案清單
                string[] testFiles = { smallFilePath, largeFilePath };

                foreach (var filePath in testFiles)
                {
                    var fileInfo = new FileInfo(filePath);
                    long fileSize = fileInfo.Length;
                    Console.WriteLine($"=== 測試檔案: {filePath} (大小: {fileSize / (1024.0 * 1024.0):F1}MB) ===");

                    // 選擇最佳演算法
                    var algorithm = FileReverseAlgorithmFactory.SelectBestAlgorithm(fileSize, availableMemoryMB, availableDiskSpaceMB);
                    var (minSize, maxSize) = algorithm.RecommendedSizeRange;

                    // 檢查檔案大小是否在推薦範圍內
                    if (fileSize < minSize || fileSize > maxSize)
                    {
                        Console.WriteLine($"檔案大小不在推薦範圍內 ({minSize / (1024.0 * 1024.0):F1}MB - {maxSize / (1024.0 * 1024.0):F1}MB)");
                        Console.WriteLine($"演算法 {algorithm.AlgorithmName} 不適用，跳過反轉。");
                        Console.WriteLine();
                        continue;
                    }

                    // 顯示演算法資訊
                    Console.WriteLine($"選擇演算法: {algorithm.AlgorithmName}");
                    Console.WriteLine($"描述: {algorithm.Description}");
                    Console.WriteLine($"推薦大小範圍: {minSize / (1024 * 1024)}MB - {maxSize / (1024 * 1024)}MB");

                    // 估計資源
                    var (memoryMB, diskSpaceMB, estimatedSeconds) = algorithm.EstimateResources(fileSize);
                    Console.WriteLine($"估計資源: 記憶體={memoryMB}MB, 磁碟={diskSpaceMB}MB, 時間={estimatedSeconds:F1}s");

                    // 反轉前：捕獲原始開頭和結尾
                    byte[] origStart = ReadBytesFromStart(filePath, ValidationSize);
                    byte[] origEnd = ReadBytesFromEnd(filePath, ValidationSize);

                    // 執行反轉
                    var result = await algorithm.ReverseFileAsync(filePath, FileEncoding.Auto, CancellationToken.None);
                    Console.WriteLine($"結果: {(result.Success ? "成功" : "失敗")}");
                    Console.WriteLine($"訊息: {result.Message}");
                    Console.WriteLine($"處理時間: {result.ProcessTime.TotalSeconds:F2}s");
                    Console.WriteLine($"偵測編碼: {result.DetectedEncoding}");
                    Console.WriteLine($"峰值記憶體使用: {result.PeakMemoryUsed / (1024 * 1024)}MB");
                    Console.WriteLine($"暫時磁碟空間使用: {result.TempDiskSpaceUsed / (1024 * 1024)}MB");
                    Console.WriteLine($"I/O 操作數: {result.IOOperations}");
                    Console.WriteLine($"處理速度: {result.ProcessingSpeed:F2}MB/s");
                    Console.WriteLine($"編碼邊界調整次數: {result.EncodingBoundaryAdjustments}");

                    // 反轉後：驗證位元組反轉
                    bool isReversed = ValidateFileReversal(filePath, origStart, origEnd);
                    Console.WriteLine($"檔案位元組反轉驗證: {(isReversed ? "正確" : "不正確")}");
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"測試失敗: {ex.Message}");
            }
            finally
            {
                // 清理
                if (Directory.Exists(testDir))
                {
                    Directory.Delete(testDir, true);
                }
            }
        }

        // 檢查 100GB 檔案並估計資源
        static void CheckAndEstimate(long fileSize, long availableMemoryMB, long availableDiskSpaceMB)
        {
            var algorithm = FileReverseAlgorithmFactory.SelectBestAlgorithm(fileSize, availableMemoryMB, availableDiskSpaceMB);
            var (minSize, maxSize) = algorithm.RecommendedSizeRange;

            Console.WriteLine($"檔案大小: {fileSize / (1024.0 * 1024.0):F1}MB");
            Console.WriteLine($"選擇演算法: {algorithm.AlgorithmName}");
            Console.WriteLine($"推薦大小範圍: {minSize / (1024 * 1024)}MB - {maxSize / (1024 * 1024)}MB");

            if (fileSize < minSize || fileSize > maxSize)
            {
                Console.WriteLine($"檔案大小不在推薦範圍內，無法反轉。");
            }
            else
            {
                var (memoryMB, diskSpaceMB, estimatedSeconds) = algorithm.EstimateResources(fileSize);
                Console.WriteLine($"估計資源: 記憶體={memoryMB}MB, 磁碟={diskSpaceMB}MB, 時間={estimatedSeconds:F1}s");
                Console.WriteLine($"檔案大小符合範圍，可執行反轉（未實際執行）。");
            }
        }

        // 建立測試檔案，使用可預測內容（重複到指定大小）
        static void CreateTestFile(string filePath, long size, FileEncoding encoding, string content)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
                Encoding enc = encoding == FileEncoding.UTF8 ? Encoding.UTF8 : Encoding.GetEncoding("Big5");
                byte[] buffer = enc.GetBytes(content);
                long written = 0;

                while (written < size)
                {
                    int toWrite = (int)Math.Min(buffer.Length, size - written);
                    fs.Write(buffer, 0, toWrite);
                    written += toWrite;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"建立測試檔案失敗 ({filePath}): {ex.Message}");
                throw;
            }
        }

        // 讀取檔案開頭位元組
        static byte[] ReadBytesFromStart(string filePath, int count)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                byte[] buffer = new byte[count];
                int read = fs.Read(buffer, 0, count);
                return buffer.Take(read).ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"讀取檔案開頭失敗 ({filePath}): {ex.Message}");
                return new byte[0];
            }
        }

        // 讀取檔案結尾位元組
        static byte[] ReadBytesFromEnd(string filePath, int count)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                fs.Seek(-count, SeekOrigin.End);
                byte[] buffer = new byte[count];
                int read = fs.Read(buffer, 0, count);
                return buffer.Take(read).ToArray();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"讀取檔案結尾失敗 ({filePath}): {ex.Message}");
                return new byte[0];
            }
        }

        // 反轉位元組陣列
        static byte[] ReverseBytes(byte[] bytes)
        {
            byte[] reversed = (byte[])bytes.Clone();
            Array.Reverse(reversed);
            return reversed;
        }

        // 驗證檔案內容是否反轉（僅位元組檢查）
        static bool ValidateFileReversal(string filePath, byte[] origStart, byte[] origEnd)
        {
            try
            {
                byte[] newStart = ReadBytesFromStart(filePath, origEnd.Length);
                byte[] newEnd = ReadBytesFromEnd(filePath, origStart.Length);

                // 位元組級檢查：新開頭應等於原始結尾的反轉，新結尾應等於原始開頭的反轉
                bool bytesValid = newStart.SequenceEqual(ReverseBytes(origEnd)) && newEnd.SequenceEqual(ReverseBytes(origStart));
                return bytesValid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"驗證檔案失敗 ({filePath}): {ex.Message}");
                return false;
            }
        }
    }
}