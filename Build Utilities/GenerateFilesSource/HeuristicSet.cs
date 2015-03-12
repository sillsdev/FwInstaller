using System.Collections.Generic;
using System.Linq;

namespace GenerateFilesSource
{
	internal sealed class HeuristicSet
	{
		internal readonly List<string> PathContains = new List<string>();
		internal readonly List<string> PathEnds = new List<string>();

		internal bool MatchFound(string path)
		{
			if (PathContains.Any(path.Contains))
				return true;
			if (PathEnds.Any(path.EndsWith))
				return true;
			return false;
		}
	}
}