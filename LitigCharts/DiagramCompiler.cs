using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LitigCharts;

/// <summary>Compiles in owned short-path temporary directories; never cleans a results directory.</summary>
public static class DiagramCompiler
{
    public static async Task CompileAllAsync(string[] sources, ArticleDiagramCommand.Configuration config, int parallelism, int passes = 1,
        string previewDirectory = null, string renderedDirectory = null)
    {
        var errors = new ConcurrentQueue<string>();
        var transientRetries = new ConcurrentQueue<string>();
        int completed = 0;
        await Parallel.ForEachAsync(sources, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, async (source, _) =>
        {
            try
            {
                await CompileAsync(source, config, passes, previewDirectory, renderedDirectory);
                Console.WriteLine($"[{Interlocked.Increment(ref completed)}/{sources.Length}] {Path.GetFileNameWithoutExtension(source)}");
            }
            catch (Exception ex) when (Retryable(ex))
            {
                // Retry timeouts and MiKTeX font-cache races once, serially after the parallel batch.
                transientRetries.Enqueue(source);
            }
            catch (Exception ex) { errors.Enqueue(source + ": " + ex.Message); }
        });
        foreach (string source in transientRetries)
        {
            try
            {
                await CompileAsync(source, config, passes, previewDirectory, renderedDirectory);
                Console.WriteLine($"[{Interlocked.Increment(ref completed)}/{sources.Length}] {Path.GetFileNameWithoutExtension(source)} (serial retry)");
            }
            catch (Exception ex) { errors.Enqueue(source + ": " + ex.Message); }
        }
        if (!errors.IsEmpty)
            throw new InvalidOperationException($"{errors.Count} compilation(s) failed; {completed} succeeded.\n" + string.Join("\n", errors));
    }

    private static bool Retryable(Exception exception) => exception is TimeoutException ||
        exception.Message.Contains("no writeable cache path", StringComparison.Ordinal) ||
        exception.InnerException != null && Retryable(exception.InnerException);

    public static async Task CompileAsync(string source, ArticleDiagramCommand.Configuration config, int passes = 1,
        string previewDirectory = null, string renderedDirectory = null, bool allPages = false)
    {
        if (passes < 1 || passes > 3) throw new ArgumentOutOfRangeException(nameof(passes));
        source = Path.GetFullPath(source);
        if (!File.Exists(source)) throw new FileNotFoundException("Missing LaTeX source.", source);
        if (renderedDirectory != null)
        {
            renderedDirectory = Path.GetFullPath(renderedDirectory);
            Directory.CreateDirectory(renderedDirectory);
        }
        string Rendered(string extension) => renderedDirectory == null
            ? ArticleResultsLayout.RenderedArtifact(source, extension)
            : Path.Combine(renderedDirectory, Path.GetFileNameWithoutExtension(source) + extension);
        var temp = Directory.CreateTempSubdirectory("acesim-diagram-");
        bool success = false;
        try
        {
            // Tables with repeated headers need their aux widths from the first pass.
            for (int pass = 0; pass < passes; pass++)
                await RunProcessAsync(config.LatexExecutable, Path.GetDirectoryName(source), config.ProcessTimeoutSeconds,
                    "--interaction=nonstopmode", "--halt-on-error", "--jobname=diagram", "--output-directory=" + temp.FullName, source);
            string pdf = Path.Combine(temp.FullName, "diagram.pdf");
            if (!File.Exists(pdf)) throw new IOException("Compiler did not produce its expected PDF.");
            await RunProcessAsync(config.PreviewExecutable, temp.FullName, config.ProcessTimeoutSeconds,
                "-png", "-singlefile", "-r", "150", pdf, Path.Combine(temp.FullName, "diagram"));
            string png = Path.Combine(temp.FullName, "diagram.png");
            if (!File.Exists(png)) throw new IOException("Preview tool did not produce its expected PNG.");
            File.Copy(pdf, Rendered(".pdf"), overwrite: true);
            string preview = previewDirectory == null ? Rendered(".png")
                : Path.Combine(Path.GetFullPath(previewDirectory), Path.GetFileNameWithoutExtension(source) + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(preview));
            File.Copy(png, preview, overwrite: true);
            if (allPages)
            {
                await RunProcessAsync(config.PreviewExecutable, temp.FullName, config.ProcessTimeoutSeconds,
                    "-png", "-r", "150", pdf, Path.Combine(temp.FullName, "page"));
                string stem = Path.GetFileNameWithoutExtension(source);
                var kept = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string page in Directory.GetFiles(temp.FullName, "page-*.png"))
                {
                    int number = int.Parse(Path.GetFileNameWithoutExtension(page)[5..]);
                    if (number == 1) continue; // The ordinary .png is the first page.
                    string destination = Path.Combine(Path.GetDirectoryName(preview), stem + $"-page-{number:00}.png");
                    File.Copy(page, destination, overwrite: true);
                    kept.Add(destination);
                }
                foreach (string page in Directory.GetFiles(Path.GetDirectoryName(preview), stem + "-page-*.png"))
                    if (!kept.Contains(page)) File.Delete(page);
            }
            success = true;
        }
        catch (Exception ex)
        {
            throw new IOException($"Diagram generation failed. Diagnostics retained in {temp.FullName}. {ex.Message}", ex);
        }
        finally
        {
            // Only the directory created by this invocation, never a caller-provided path.
            if (success && temp.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar)
                    .Equals(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                && !temp.Attributes.HasFlag(FileAttributes.ReparsePoint))
                temp.Delete(recursive: true);
        }
    }

    public static async Task RunProcessAsync(string executable, string workingDirectory, int timeoutSeconds, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + executable);
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw new TimeoutException($"{executable} exceeded {timeoutSeconds} seconds.");
        }
        string output = await stdout + "\n" + await stderr;
        if (process.ExitCode != 0)
            throw new IOException($"{executable} exited {process.ExitCode}.\n" + output[Math.Max(0, output.Length - 4000)..]);
    }
}
