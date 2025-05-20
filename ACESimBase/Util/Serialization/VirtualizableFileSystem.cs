using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ACESimBase.Util.Serialization
{
    public sealed class VirtualizableFileSystem
    {
        public readonly bool IsReal;
        private readonly string _root;
        private readonly HashSet<string> _files;
        private readonly HashSet<string> _dirs;

        public VirtualizableFileSystem(string rootPath, bool useReal = true)
        {
            _root = Path.GetFullPath(rootPath);
            IsReal = useReal;
            _files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { _root };

            if (!System.IO.Directory.Exists(_root))
                System.IO.Directory.CreateDirectory(_root);

            if (!IsReal)
                ScanRealFileSystem();

            File = new FileFacade(this);
            Directory = new DirectoryFacade(this);
        }

        /* ---------- Facades ---------- */

        public FileFacade File { get; }
        public DirectoryFacade Directory { get; }

        public sealed class FileFacade
        {
            private readonly VirtualizableFileSystem _fs;
            internal FileFacade(VirtualizableFileSystem fs) => _fs = fs;

            public bool Exists(string path) =>
                _fs.IsReal
                    ? System.IO.File.Exists(_fs.Abs(path))
                    : _fs._files.Contains(_fs.Abs(path));

            public void Copy(string source, string destination, bool overwrite = false)
            {
                var src = _fs.Abs(source);
                var dst = _fs.Abs(destination);

                if (_fs.IsReal)
                {
                    System.IO.File.Copy(src, dst, overwrite);
                    return;
                }

                if (!_fs._files.Contains(src))
                    throw new FileNotFoundException(source);

                if (!overwrite && _fs._files.Contains(dst))
                    throw new IOException("Destination exists.");

                _fs._dirs.Add(Path.GetDirectoryName(dst)!);
                _fs._files.Add(dst);
            }

            public void Delete(string path)
            {
                var p = _fs.Abs(path);
                if (_fs.IsReal)
                    System.IO.File.Delete(p);
                else
                    _fs._files.Remove(p);
            }

            public void Add(string path)
            {
                if (_fs.IsReal) return;

                var p = _fs.Abs(path);
                var dir = Path.GetDirectoryName(p)!;

                _fs._dirs.Add(dir);
                _fs._files.Add(p);
            }
            public void Move(string source, string destination, bool overwrite = false)
            {
                var src = _fs.Abs(source);
                var dst = _fs.Abs(destination);

                if (_fs.IsReal)
                {
                    System.IO.File.Move(src, dst, overwrite);
                    return;
                }

                if (!_fs._files.Contains(src))
                    throw new FileNotFoundException(source);

                if (_fs._files.Contains(dst))
                {
                    if (!overwrite)
                        throw new IOException("Destination exists.");
                    _fs._files.Remove(dst);
                }

                _fs._files.Remove(src);
                _fs._dirs.Add(Path.GetDirectoryName(dst)!);
                _fs._files.Add(dst);
            }

        }

        public sealed class DirectoryFacade
        {
            private readonly VirtualizableFileSystem _fs;
            internal DirectoryFacade(VirtualizableFileSystem fs) => _fs = fs;

            public string[] GetFiles(string directory) =>
                _fs.IsReal
                    ? System.IO.Directory.GetFiles(_fs.Abs(directory))
                    : _fs._files.Where(f => Path.GetDirectoryName(f)!
                            .Equals(_fs.Abs(directory), StringComparison.OrdinalIgnoreCase))
                            .ToArray();

            public IEnumerable<string> EnumerateFiles(string directory) =>
                _fs.IsReal
                    ? System.IO.Directory.EnumerateFiles(_fs.Abs(directory))
                    : _fs._files.Where(f => Path.GetDirectoryName(f)!
                            .Equals(_fs.Abs(directory), StringComparison.OrdinalIgnoreCase));
            public string[] GetDirectories(string directory)
            {
                var dirAbs = _fs.Abs(directory);

                if (_fs.IsReal)
                    return System.IO.Directory.GetDirectories(dirAbs);

                return _fs._dirs
                          .Where(d => !d.Equals(dirAbs, StringComparison.OrdinalIgnoreCase) &&
                                      Path.GetDirectoryName(d)!
                                          .Equals(dirAbs, StringComparison.OrdinalIgnoreCase))
                          .ToArray();
            }

            public void CreateDirectory(string directory)
            {
                var dir = _fs.Abs(directory);
                if (_fs.IsReal)
                    System.IO.Directory.CreateDirectory(dir);
                else
                    _fs._dirs.Add(dir);
            }
        }

        /* ---------- Internals ---------- */

        private void ScanRealFileSystem()
        {
            foreach (var d in System.IO.Directory.GetDirectories(_root, "*", SearchOption.AllDirectories))
                _dirs.Add(Path.GetFullPath(d));

            foreach (var f in System.IO.Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                _files.Add(Path.GetFullPath(f));
        }

        private string Abs(string path) =>
            Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_root, path));
    }

}
