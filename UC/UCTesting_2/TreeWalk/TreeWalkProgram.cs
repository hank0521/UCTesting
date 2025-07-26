using System;

namespace TreeWalkApp
{
    class Program
    {
        static void Main(string[] args)
        {
            // 測試用例：題目給定的樹
            string input = "A,B,C,,D,E,F,,,,,,,G";

            //string input = "P,Q,R,S,,T,,U,V";

            Console.WriteLine($"輸入樹: {input}");
            var service = new TreePathService(input);

            // 測試 1: B 到 F，期望輸出 "上右右"
            TestPath(service, "B", "F");
            //
            //// 測試 2: B 到 B，期望輸出空字符串
            TestPath(service, "B", "B");
            //
            //// 測試 3: 無效節點 Z 到 F，期望異常
            TestPath(service, "Z", "F");

            // 交互式測試
            Console.WriteLine("\n輸入查詢 (格式: 起點 終點，例如 B F)，輸入 'exit' 退出：");
            while (true)
            {
                Console.Write("查詢: ");
                string query = Console.ReadLine();
                if (query?.ToLower() == "exit") break;

                var parts = query?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts?.Length != 2)
                {
                    Console.WriteLine("格式錯誤，請輸入：起點 終點");
                    continue;
                }

                TestPath(service, parts[0], parts[1]);
            }
        }

        static void TestPath(TreePathService service, string start, string end)
        {
            try
            {
                string path = service.GetPath(start, end);
                Console.WriteLine($"從 {start} 到 {end} 的路徑: {(string.IsNullOrEmpty(path) ? "無需移動" : path)}");
            }
            catch (ArgumentException ex)
            {
                Console.WriteLine($"錯誤: {ex.Message}");
            }
        }
    }
}