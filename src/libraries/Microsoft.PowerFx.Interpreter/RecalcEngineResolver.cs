﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.PowerFx.Core.Binding;
using Microsoft.PowerFx.Core.Binding.BindInfo;
using Microsoft.PowerFx.Core.Glue;
using Microsoft.PowerFx.Core.IR.Symbols;
using Microsoft.PowerFx.Core.Utils;

namespace Microsoft.PowerFx
{
    /// <summary>
    /// <see cref="INameResolver"/> implementation for <see cref="RecalcEngine"/>.
    /// </summary>
    internal class RecalcEngineResolver : SimpleResolver
    {
        private readonly RecalcEngine _parent;
        private readonly PowerFxConfig _powerFxConfig;

        public RecalcEngineResolver(RecalcEngine parent, PowerFxConfig powerFxConfig)
            : base(powerFxConfig)
        {
            _parent = parent;
            _powerFxConfig = powerFxConfig;
        }

        public override bool Lookup(DName name, out NameLookupInfo nameInfo, NameLookupPreferences preferences = NameLookupPreferences.None)
        {
            // Kinds of globals:
            // - global formula
            // - parameters 
            // - environment symbols

            var str = name.Value;

            if (_parent.Formulas.TryGetValue(str, out var fi))
            {
                var data = fi;
                var type = fi._type._type;

                nameInfo = new NameLookupInfo(
                    BindKind.PowerFxResolvedObject,
                    type,
                    DPath.Root,
                    0,
                    data);
                return true;
            }

            return base.Lookup(name, out nameInfo, preferences);
        }

        public override void SetGlobalSymbols(ImmutableDictionary<string, GlobalSymbol> globalSymbols = null)
        {
            base.SetGlobalSymbols(globalSymbols ?? _parent.Formulas
                .Select(f => new GlobalSymbol(f.Key, $"{f.Key} variable", f.Value.Value.Type))
                .ToImmutableDictionary(kvp => kvp.Name, kvp => kvp));
        }

        public class ParameterData
        {
            public string ParameterName;
        }
    }
}
