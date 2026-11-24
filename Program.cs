namespace Spacewise;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // hidden test mode: Spacewise.exe --benchmark C:\ out.txt  (scans, writes the time and totals, exits)
        if (args.Length >= 3 && args[0] == "--benchmark")
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var root = Scanner.Scan(args[1], (_, _, _) => { }, CancellationToken.None);
            File.WriteAllText(args[2], $"{sw.Elapsed.TotalSeconds:0.0}s {root.Files} files {Scanner.Human(root.Size)}");
            return;
        }
        // hidden test mode: Spacewise.exe --explain out.txt <path>...  (writes each path's label and explanation, exits)
        if (args.Length >= 3 && args[0] == "--explain")
        {
            File.WriteAllLines(args[1], args.Skip(2).Select(p => { var i = Knowledge.Explain(p, Directory.Exists(p)); return $"{i.Verdict,-9} {p}\n          {i.What}\n          {i.Ar}"; }));
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
