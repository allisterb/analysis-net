// Copyright (c) Edgardo Zoppi. All Rights Reserved.
// See License.txt in the repository root directory for license information.

using Model;
using Model.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cci = Microsoft.Cci;

namespace CCIProvider
{
	public class Loader : ILoader
	{
		private Host ourHost;
		private Cci.MetadataReaderHost cciHost;
		// Silvergun: whether a .pdb beside the assembly is read for source locations. A PDB is one more untrusted parser
		// (CCI's PdbReader and the DiaSymReader converter, a separate compilation whose Debug.Assert can FailFast), so a
		// caller that needs no source locations turns it off (ADR 0022 Stage 1).
		private readonly bool readPdbs;

		public Loader(Host host) : this(host, true)
		{
		}

		// Silvergun: readPdbs = false skips the sibling .pdb entirely.
		public Loader(Host host, bool readPdbs)
		{
			this.ourHost = host;
			this.cciHost = new Cci.PeReader.DefaultHost();
			this.readPdbs = readPdbs;
		}

		public void Dispose()
		{
			this.cciHost.Dispose();
			this.cciHost = null;
			this.ourHost = null;
			GC.SuppressFinalize(this);
		}

		public Host Host
		{
			get { return ourHost; }
		}

		public Assembly LoadCoreAssembly()
		{
			var module = cciHost.LoadUnit(cciHost.CoreAssemblySymbolicIdentity) as Cci.IModule;

			if (module == null || module == Cci.Dummy.Module || module == Cci.Dummy.Assembly)
				throw new Exception("The input is not a valid CLR module or assembly.");

			var assembly = this.ExtractAssembly(module, null);

			ourHost.Assemblies.Add(assembly);
			return assembly;
		}

		public Assembly LoadAssembly(string fileName)
		{
			var module = cciHost.LoadUnitFrom(fileName) as Cci.IModule;

			if (module == null || module == Cci.Dummy.Module || module == Cci.Dummy.Assembly)
				throw new Exception("The input is not a valid CLR module or assembly.");

			var pdbFileName = Path.ChangeExtension(fileName, "pdb");
			Cci.PdbReader pdbReader = null;

			if (this.readPdbs && File.Exists(pdbFileName)) // Silvergun: readPdbs
			{
				
				try
				{
					pdbReader = new Cci.PdbReader(fileName, pdbFileName, cciHost, true);
				}
				catch (Exception ex)
				{
					// TODO: Do something with the exception.
				}
				
			}

			var assembly = this.ExtractAssembly(module, pdbReader);

			if (pdbReader != null)
			{
				pdbReader.Dispose();
			}

			ourHost.Assemblies.Add(assembly);
			return assembly;
		}

		private Assembly ExtractAssembly(Cci.IModule module, Cci.PdbReader pdbReader)
		{
			var traverser = new AssemblyTraverser(ourHost, cciHost, pdbReader);
			traverser.Traverse(module.ContainingAssembly);
			var result = traverser.Result;
			return result;
		}
	}
}
