using System;
using System.IO;
using System.Linq;

namespace GenerateFilesSource
{
	internal sealed class CabinetMappingData
	{
		private readonly int _index;
		private readonly int[] _indexes;
		private readonly string[] _divisions;

		internal CabinetMappingData(string index, string indexes, string divisions)
		{
			if (index.Length == 0)
			{
				if (indexes.Length == 0 || divisions.Length == 0)
					throw new InvalidDataException(
						"Invalid CabinetAssignment node: when CabinetIndex is not assigned, both CabinetIndexes and CabinetDivisions must be assigned.");
				_index = 0;

				var indexStrings = indexes.Split(new[] { ',', ';' });

				_indexes = new int[indexStrings.Length];
				for (var i = 0; i < indexStrings.Length; i++)
					_indexes[i] = Int32.Parse(indexStrings[i]);

				_divisions = divisions.ToLowerInvariant().Split(new[] { ',', ';' });

				if (_indexes.Length != _divisions.Length + 1)
					throw new InvalidDataException("Invalid CabinetAssignment node: \"CabinetIndexes=\"" + indexes +
						" \"CabinetDivisions=\"" + divisions +
						"\" is wrong because there must be one more index than division.");
				return;
			}

			if (indexes.Length != 0 || divisions.Length != 0)
				throw new InvalidDataException(
					"Invalid CabinetAssignment node: when CabinetIndex is assigned, neither CabinetIndexes nor CabinetDivisions may be assigned.");

			_index = Int32.Parse(index);
		}

		internal int GetCabinet(InstallerFile file)
		{
			if (_index != 0)
				return _index;

			for (var index = 0; index < _divisions.Length; index++)
			{
				if (file.Name.ToLowerInvariant().CompareTo(_divisions[index]) < 0)
					return _indexes[index];
			}
			return _indexes.Last();
		}
	}
}