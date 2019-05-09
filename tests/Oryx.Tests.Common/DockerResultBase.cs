// --------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license.
// --------------------------------------------------------------------------------------------

using Microsoft.Oryx.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Oryx.Tests.Common
{
    public abstract class DockerResultBase
    {
        private readonly Object Lock1 = new Object();
        private readonly Object Lock2 = new Object();
        public DockerResultBase(Exception exc, string executedCmd)
        {
            Exception = exc;
            ExecutedCommand = executedCmd;
        }

        public Exception Exception { get; }

        public string ExecutedCommand { get; }

        public abstract bool HasExited { get; }

        public abstract int ExitCode { get; }

        public abstract string StdOut { get; }

        public abstract string StdErr { get; }

        public virtual string GetDebugInfo(IDictionary<string, string> extraDefs = null)
        {

            var infoFormatter = new DefinitionListFormatter();
            var infoFormatterString = string.Empty;
            var result = string.Empty;
            var sb = new StringBuilder();

            infoFormatter.AddDefinition("Executed command", ExecutedCommand);
            if (HasExited) infoFormatter.AddDefinition("Exit code", ExitCode.ToString());
            infoFormatter.AddDefinition("StdOut", StdOut);
            infoFormatter.AddDefinition("StdErr", StdErr);
            infoFormatter.AddDefinition("Exception.Message:", Exception?.Message);
            infoFormatterString = infoFormatter.ToString();

            lock (this.Lock1)
            {
                sb.AppendLine();
                sb.AppendLine("Debugging Information:");
                sb.AppendLine("----------------------");
                sb.AppendLine(infoFormatterString);
            }

            lock (this.Lock2)
            {
                result = sb.ToString();
            }
            
            return result;
        }
    }
}
