namespace ArticleReplication;

/// <summary>Portable collection indexes generated from the plan and actual outputs.</summary>
public static class CollectionDocumentation
{
    public static void Generate(ResolvedArticlePlan plan,string collection,int profiles,string work)
    {
        var packaging=SolutionPathPackaging.Package(Path.Combine(collection,"Supplemental materials/Equilibrium solution paths"));
        string packagingRecord=Path.Combine(work,"solution-path-packaging.json");
        if(packaging.Length>0||!File.Exists(packagingRecord))Files.Save(packagingRecord,new{Passed=true,Packaged=packaging,NoFramesRemoved=true,NoNumericalValuesChanged=true},replace:true);
        // Keep execution/renderer receipts outside the reader-facing collection.
        string records=Path.Combine(collection,"Results/Run records");
        if(Directory.Exists(records))
        {
            foreach(string file in Directory.GetFiles(records,"*",SearchOption.AllDirectories))
            {
                string target=Files.Under(Path.Combine(work,"ReportResults/Collection records"),Path.GetRelativePath(records,file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Move(file,target);
            }
        }
        void Write(string relative,string text)
        {
            string file=Files.Under(collection,relative);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,text.Replace("\r\n","\n").Trim()+"\n");
        }
        string Link(string relative)=>string.Join('/',relative.Replace('\\','/').Split('/').Select(Uri.EscapeDataString));
        string Index(string directory,string pattern)
        {
            string root=Files.Under(collection,directory);
            return Directory.Exists(root)?string.Join('\n',Directory.GetFiles(root,pattern).Order(StringComparer.Ordinal)
                .Select(f=>$"- [{Path.GetFileNameWithoutExtension(f)}]({Link(Path.GetFileName(f))})")):"This stage was not requested.";
        }
        string grids=string.Join("; ",plan.Settings.Grids.Select(g=>$"{g.Signals} signals / {g.Offers} offers ({g.Risk switch {"rn"=>"risk-neutral","ra"=>"risk-averse",_=>"both risk preferences"}})"));
        string articleLink=plan.Steps.Contains("Manuscript")?"[Article PDF](Article%20and%20bibliography/corr_signals.pdf)":"[Author-maintained article](https://github.com/mbabramo/correlated-signals-article/tree/main/Article%20and%20bibliography)";
        Write(".gitignore","# Local rebuilds and editor/compiler intermediates\n.reproduction/\n*.aux\n*.log\n*.out\n*.bbl\n*.blg\n*.bcf\n*.run.xml\n*.synctex.gz\n*-blx.bib\n");
        Write(".gitattributes","# Keep generated data and PDF/source assets byte-identical across checkouts.\n* -text -ident\n");
        Write("README.md",$$"""
            # Correlated signals in litigation

            {{articleLink}} · [Figures](Figures/README.md) · [Tables](Tables/README.md)

            This collection contains {{profiles}} validated primary profiles from the {{plan.Cases.Length}}-case resolved article plan. The grid comparisons are {{grids}}. American and British denote the principal rules; trial-only fee shifting is a separate extension.

            - **Figures** and **Tables**: exhibits included in the article, with editable sources and previews.
            - **Results/Individual simulations**: complete strategies, audits, numerical reports and standard diagrams for every reported game.
            - **Results/Aggregated Data**: matched comparisons, welfare measures, truth-formula sensitivity and tremble responses.
            - **Supplemental materials**: multiple-equilibrium results, decompositions, solution-path viewers, signal and game-tree diagrams, and utility curves.

            The **Article and bibliography** folder is author-maintained and separate from default replication. The journal command generates the four research-output folders above. An optional `--manuscript true` author build also compiles the embedded manuscript snapshot with generated numerical bindings; it never overwrites the author's checkout.

            ## Replication

            Use the `ArticleReplication` C# project in the [ACESim4 correlated-signals branch](https://github.com/mbabramo/ACESim4/tree/correlated-signals). Follow its [installation instructions](https://github.com/mbabramo/ACESim4/blob/correlated-signals/ArticleReplication/INSTALL.md) for .NET, TeX, fonts and PDF tools, or use its container build target. Tools are installed separately.

            Download and extract the optional saved-solutions archive from the [article repository releases](https://github.com/mbabramo/correlated-signals-article/releases). From the code checkout, run:

            ```sh
            dotnet run --project ArticleReplication -c Release -- rebuild --source . --output /path/new-rebuild --input /path/saved-solutions --missing wait --workers 4
            ```

            Read the collection in `new-rebuild/run/article`. Remove `--input` and use `--missing compute` for a complete fresh calculation, which can take substantially longer. Settings and stage switches are documented in the [coordinator README](https://github.com/mbabramo/ACESim4/blob/correlated-signals/ArticleReplication/README.md). Worker counts must account for other active computations.

            Shortcuts contain only complete primary equilibria, the {{plan.ExpectedApproximateStarts}} multiple-start outcomes (including explicit failed attempts), and optional solver histories. Every accepted profile is revalidated; histories are replay-checked. Decompositions, tremble experiments, reports and exhibits are freshly generated. The manuscript PDF is compiled only when explicitly requested. Failed searches are not proofs of nonexistence. Exact-primary, approximate-search and trajectory-replay criteria remain distinct.

            The case inventory is [selected-primary-catalog.json](Results/Aggregated%20Data/selected-primary-catalog.json). Temporary build, execution and release-review records belong outside this published collection.
            """);
        Write("Figures/README.md","# Article figures\n\nOnly figures included in the manuscript are numbered here. Editable TeX/data are in `Sources`.\n\n"+Index("Figures","*.pdf"));
        Write("Tables/README.md","# Article tables\n\nEditable TeX and data are in `Sources`. Figure 7 replaces the former welfare table; numbering follows the manuscript.\n\n"+Index("Tables","*.pdf"));
        if(plan.Steps.Contains("Manuscript"))Write("Article and bibliography/README.md","# Article and bibliography\n\n[corr_signals.pdf](corr_signals.pdf) is compiled from [corr_signals.tex](corr_signals.tex) and [corr_signals.bib](corr_signals.bib), with the generated exhibits in the sibling Tables and Figures folders. The current authored sources are embedded in the ArticleReplication C# project on the ACESim4 correlated-signals branch. Numerical result bindings are filled from current validated data in [generated-values.tex](generated-values.tex). Follow the collection's root README to rebuild the complete collection. Historical author backups and build intermediates are archived separately, outside the generated collection.\n");
        string supplemental=Path.Combine(collection,"Supplemental materials");Directory.CreateDirectory(supplemental);
        Write("Supplemental materials/README.md","# Supplemental materials\n\n"+string.Join('\n',Directory.GetDirectories(supplemental).Order(StringComparer.Ordinal).Select(d=>$"- [{Path.GetFileName(d)}]({Link(Path.GetFileName(d))}/)")));
        if(plan.Steps.Contains("Histories"))Write("Supplemental materials/Equilibrium solution paths/README.md","# Equilibrium solution paths\n\nThe four core-game viewers retain complete recorded strategies and native solver coordinates. Each saved frame is replay-checked against the current game and final equilibrium; this is distinct from an algebraic proof of every tableau pivot. Open these HTML files locally in a current Chrome, Edge or Firefox browser. Keep any adjacent `-data` folder beside its HTML file: large traces use local script chunks to stay below repository file limits. The compressed payload is reconstructed exactly, with no omitted frames or rounded values.\n\n"+Index("Supplemental materials/Equilibrium solution paths","*.html"));
        Files.Save(Path.Combine(collection,"Results/Aggregated Data/reporting-inventory.json"),new{
            PrimaryCases=plan.Cases.Select(c=>c.Id),AvailableProfiles=profiles,ExpectedPrimaryCases=plan.Cases.Length,
            WelfareComparisons=plan.Welfare,StrategicComparisons=plan.Strategic,ExpectedSearchAttempts=plan.ExpectedApproximateStarts,
            Files=Directory.GetFiles(collection,"*",SearchOption.AllDirectories).Select(f=>Path.GetRelativePath(collection,f).Replace('\\','/')).Order(StringComparer.Ordinal).ToArray()},replace:true);
    }

    public static void FromRun(string run)
    {
        if(Files.Object(Path.Combine(run,"completed.json"))["Passed"]?.GetValue<bool>()!=true)
            throw new InvalidDataException("A completed validated run is required.");
        var plan=Files.Read<ResolvedArticlePlan>(Path.Combine(run,"resolved-plan.json"));
        int count=Files.Object(Path.Combine(run,"completed.json"))["PrimaryProfiles"]!.GetValue<int>();
        Generate(plan,Path.Combine(run,"article"),count,run);
    }
}
