﻿using System;
using System.Collections.Generic;
using uTinyRipper.Layout;
using uTinyRipper.SerializedFiles;

namespace uTinyRipper
{
	internal sealed class GameStructureProcessor : IDisposable
	{
		~GameStructureProcessor()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			GC.SuppressFinalize(this);
			Dispose(true);
		}

		public void AddScheme(string filePath, string fileName)
		{
			FileScheme scheme = GameCollection.LoadScheme(filePath, fileName);
			OnSchemeLoaded(scheme);
			m_schemes.Add(scheme);
		}

		public void AddDependencySchemes(Func<string, string> dependencyCallback)
		{
			for (int i = 0; i < m_schemes.Count; i++)
			{
				FileScheme scheme = m_schemes[i];
				foreach (FileIdentifier dependency in scheme.Dependencies)
				{
					if (m_knownFiles.Contains(dependency.FilePath))
					{
						continue;
					}

					string systemFilePath = dependencyCallback.Invoke(dependency.FilePath);
					if (systemFilePath == null)
					{
						m_knownFiles.Add(dependency.FilePath);
						Logger.Log(LogType.Warning, LogCategory.Import, $"Dependency '{dependency}' hasn't been found");
						continue;
					}

					AddScheme(systemFilePath, dependency.FilePath);
				}
			}
		}

		public void ProcessSchemes(GameCollection fileCollection)
		{
			GameProcessorContext context = new GameProcessorContext(fileCollection);
			foreach (FileScheme scheme in m_schemes)
			{
				fileCollection.AddFile(context, scheme);
			}
			context.ReadSerializedFiles();
		}

		public LayoutInfo GetLayoutInfo()
		{
			SerializedFileScheme prime = GetPrimaryFile();
			if (prime != null)
			{
				return GetLayoutInfo(prime);
			}

			SerializedFileScheme serialized = GetEngineFile();
			return GetLayoutInfo(serialized);
		}

		private static IEnumerable<FileScheme> EnumerateSchemes(IReadOnlyList<FileScheme> schemes)
		{
			foreach (FileScheme scheme in schemes)
			{
				yield return scheme;
				if (scheme is FileSchemeList fileList)
				{
					foreach (FileScheme nestedScheme in EnumerateSchemes(fileList.Schemes))
					{
						yield return nestedScheme;
					}
				}
				else if (scheme.SchemeType == FileEntryType.Archive)
				{
					ArchiveFileScheme archive = (ArchiveFileScheme)scheme;
					yield return archive.WebScheme;
					foreach (FileScheme nestedScheme in EnumerateSchemes(archive.WebScheme.Schemes))
					{
						yield return nestedScheme;
					}
				}
			}
		}

		private static Version GetDefaultGenerationVersions(FileGeneration generation)
		{
			if (generation < FileGeneration.FG_120_200)
			{
				return new Version(1, 2, 2);
			}

			switch (generation)
			{
				case FileGeneration.FG_120_200:
					return new Version(1, 6);
				case FileGeneration.FG_210_261:
					return new Version(2, 5);
				case FileGeneration.FG_300b:
					return new Version(3, 0, 0, VersionType.Beta, 1);
				default:
					throw new NotSupportedException();
			}
		}

		private void Dispose(bool disposing)
		{
			foreach (FileScheme scheme in m_schemes)
			{
				scheme.Dispose();
			}
		}

		private LayoutInfo GetLayoutInfo(SerializedFileScheme serialized)
		{
			if (RTTIClassHierarchyDescriptor.HasPlatform(serialized.Header.Generation))
			{
				RTTIClassHierarchyDescriptor hierarchy = serialized.Metadata.Hierarchy;
				return new LayoutInfo(hierarchy.Version, hierarchy.Platform, TransferInstructionFlags.SerializeGameRelease);
			}
			else
			{
				const Platform DefaultPlatform = Platform.StandaloneWinPlayer;
				const TransferInstructionFlags DefaultFlags = TransferInstructionFlags.SerializeGameRelease;
				BundleFileScheme bundle = GetBundleFile();
				if (bundle == null)
				{
					Logger.Log(LogType.Warning, LogCategory.Import, "Unable to determine layout for provided files. Tring default one");
					Version version = GetDefaultGenerationVersions(serialized.Header.Generation);
					return new LayoutInfo(version, DefaultPlatform, DefaultFlags);

				}
				else
				{
					Logger.Log(LogType.Warning, LogCategory.Import, "Unable to precisly determine layout for provided files. Tring default one");
					return new LayoutInfo(bundle.Header.EngineVersion, DefaultPlatform, DefaultFlags);
				}
			}
		}

		private SerializedFileScheme GetPrimaryFile()
		{
			foreach (FileScheme scheme in EnumerateSchemes(m_schemes))
			{
				if (scheme.SchemeType == FileEntryType.Serialized)
				{
					if (PlatformGameStructure.IsPrimaryEngineFile(scheme.NameOrigin))
					{
						return (SerializedFileScheme)scheme;
					}
				}
			}
			return null;
		}

		private SerializedFileScheme GetEngineFile()
		{
			foreach (FileScheme scheme in EnumerateSchemes(m_schemes))
			{
				if (scheme.SchemeType == FileEntryType.Serialized)
				{
					return (SerializedFileScheme)scheme;
				}
			}
			return null;
		}

		private BundleFileScheme GetBundleFile()
		{
			foreach (FileScheme scheme in EnumerateSchemes(m_schemes))
			{
				if (scheme.SchemeType == FileEntryType.Bundle)
				{
					return (BundleFileScheme)scheme;
				}
			}
			return null;
		}

		private void OnSchemeLoaded(FileScheme scheme)
		{
			if (scheme.SchemeType == FileEntryType.Serialized)
			{
				m_knownFiles.Add(scheme.Name);
			}

			if (scheme is FileSchemeList list)
			{
				foreach (FileScheme nestedScheme in list.Schemes)
				{
					OnSchemeLoaded(nestedScheme);
				}
			}
		}

		private readonly List<FileScheme> m_schemes = new List<FileScheme>();
		private readonly HashSet<string> m_knownFiles = new HashSet<string>();
	}
}
