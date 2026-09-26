using System.Text.Json.Nodes;

namespace ArticleReplication;

public enum ReplicationMode { FromSolutions,FromScratch }
public enum JobDisposition { ReuseValidated,Compute,AwaitExternalResult,MissingResult,Skipped }
public sealed record ExternalJob(string CaseId,string ScientificGameSha256,string Stage,string Disposition);
public sealed record ExternalJobs(string Schema,ExternalJob[] Cases);
public sealed record StoredFile(string Path,string Sha256);
public sealed record StoredArtifact(string Stage,string CaseId,string ScienceIdentity,string Contract,
    StoredFile[] Files,StoredFile Receipt,string[] Dependencies);

public static class Reuse
{
    public static JobDisposition Decide(string stage,string caseId,string gameHash,ReplicationMode mode,bool enabled,
        bool validCache,bool computeMissing,IReadOnlyList<ExternalJob> external)
    {
        if(!enabled)return JobDisposition.Skipped;
        // A valid completed cache can be imported, but a changed name must not bypass a running game's reservation.
        if(mode==ReplicationMode.FromSolutions&&validCache)return JobDisposition.ReuseValidated;
        if(external.Any(x=>x.Stage==stage&&(x.CaseId==caseId||x.ScientificGameSha256.Equals(gameHash,StringComparison.OrdinalIgnoreCase))))
            return JobDisposition.AwaitExternalResult;
        if(mode==ReplicationMode.FromScratch||computeMissing)return JobDisposition.Compute;
        return JobDisposition.MissingResult;
    }

    public static void VerifyArtifact(string root,StoredArtifact artifact,string expectedScience,string expectedContract)
    {
        if(artifact.ScienceIdentity!=expectedScience||artifact.Contract!=expectedContract)throw new InvalidDataException("Incompatible cached artifact.");
        if(artifact.Files.Length==0)throw new InvalidDataException("Empty artifact.");
        foreach(var file in artifact.Files.Append(artifact.Receipt))
            if(!Files.Sha(Files.Under(root,file.Path)).Equals(file.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Changed cached artifact: "+file.Path);
        var receipt=Files.Object(Files.Under(root,artifact.Receipt.Path));
        if(receipt["Passed"]?.GetValue<bool>()!=true)throw new InvalidDataException("Cached stage has no passed validation receipt.");
        // This is integrity/provenance validation. Call the stage-specific scientific validator before reuse.
    }
}
