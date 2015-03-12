namespace GenerateFilesSource
{
	internal sealed class FileHeuristics
	{
		internal readonly HeuristicSet Inclusions = new HeuristicSet();
		internal readonly HeuristicSet Exclusions = new HeuristicSet();

		internal bool IsFileIncluded(string path)
		{
			if (Exclusions.MatchFound(path))
				return false;
			if (Inclusions.MatchFound(path))
				return true;
			return false;
		}
	}
}