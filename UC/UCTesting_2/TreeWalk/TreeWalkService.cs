using System;
using System.Collections.Generic;

namespace TreeWalkApp
{
    public class TreePathService
    {
        private readonly List<string> _nodes; // 陣列表示樹，從 index 1 開始 (根)
        private readonly Dictionary<string, int> _valToIndex; // 值到索引的映射
        private readonly int[] _parent; // parent[i] = 父節點索引
        private readonly int[] _depth; // depth[i] = 深度
        private readonly int[,] _jumps; // Binary lifting for LCA: jumps[i,k] = 2^k 祖先
        private readonly int _maxLog; // log2(1024) ~ 10

        public TreePathService(string input)
        {
            _nodes = new List<string> { null }; // index 0 dummy
            var parts = input.Split(',');
            foreach (var part in parts)
            {
                _nodes.Add(string.IsNullOrEmpty(part) ? null : part.Trim());
            }

            int n = _nodes.Count - 1; // 有效節點數
            _valToIndex = new Dictionary<string, int>();
            _parent = new int[n + 1];
            _depth = new int[n + 1];
            _maxLog = 10; // 深度 <=10，log 足夠
            _jumps = new int[n + 1, _maxLog + 1];

            // 構建 parent, depth, valToIndex
            for (int i = 1; i <= n; i++)
            {
                if (_nodes[i] != null)
                {
                    _valToIndex[_nodes[i]] = i;
                }
                _parent[i] = i / 2;
                _depth[i] = _depth[_parent[i]] + 1;
            }

            // 預處理 binary lifting
            for (int i = 1; i <= n; i++)
            {
                _jumps[i, 0] = _parent[i];
            }
            for (int k = 1; k <= _maxLog; k++)
            {
                for (int i = 1; i <= n; i++)
                {
                    int mid = _jumps[i, k - 1];
                    _jumps[i, k] = (mid == 0) ? 0 : _jumps[mid, k - 1];
                }
            }
        }

        public string GetPath(string start, string end)
        {
            if (!_valToIndex.TryGetValue(start, out int u) || !_valToIndex.TryGetValue(end, out int v))
            {
                throw new ArgumentException("Invalid start or end node");
            }

            if (u == v) return string.Empty;

            // 找 LCA
            int lca = FindLCA(u, v);

            // 從 start 到 LCA: "上" 次數 = depth[u] - depth[lca]
            string pathToLCA = new string('上', _depth[u] - _depth[lca]);

            // 從 LCA 到 end: 向下 "左" or "右"
            List<char> pathFromLCA = new List<char>();
            int current = v;
            while (current != lca)
            {
                int par = _parent[current];
                pathFromLCA.Add(current == par * 2 ? '左' : '右');
                current = par;
            }
            pathFromLCA.Reverse(); // 從 LCA 向下，所以反轉

            return pathToLCA + new string(pathFromLCA.ToArray());
        }

        private int FindLCA(int u, int v)
        {
            if (_depth[u] > _depth[v]) (u, v) = (v, u); // 確保 u 較淺

            // 讓 v 跳到與 u 同深
            int diff = _depth[v] - _depth[u];
            for (int k = 0; k <= _maxLog; k++)
            {
                if ((diff & (1 << k)) != 0)
                {
                    v = _jumps[v, k];
                }
            }

            if (u == v) return u;

            // 同時跳
            for (int k = _maxLog; k >= 0; k--)
            {
                if (_jumps[u, k] != _jumps[v, k])
                {
                    u = _jumps[u, k];
                    v = _jumps[v, k];
                }
            }

            return _parent[u];
        }
    }
}