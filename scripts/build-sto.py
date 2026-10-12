"""Build a matched .NET 10 converter/serializer bundle from explicit schema inputs.

Requires Python 3 and the .NET 10 SDK.
Generated assemblies are not byte deterministic (PersistedAssemblyBuilder creates an MVID).
The manifest records exact inputs and outputs for each run.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def git(*args):
    return subprocess.check_output(['git', '-C', str(ROOT), *args], text=True).strip()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--build', required=True)
    parser.add_argument('--schema-dir', required=True, type=Path)
    parser.add_argument('--schema-id', required=True, help='Explicit export/repair identifier')
    parser.add_argument('--mappings', required=True, type=Path,
                        help='JSON resource mapping list; hashes must match the supplied BINs')
    parser.add_argument('--output', type=Path, default=ROOT / 'bin-net10')
    args = parser.parse_args()
    for value in (args.build, args.schema_id):
        if not re.fullmatch(r'[A-Za-z0-9_.-]+', value):
            parser.error('Build and schema identifier must be safe filename components')
    schemas = args.schema_dir.resolve()
    if not (schemas / 'parses').is_dir() or not (schemas / 'enums').is_dir():
        parser.error('Schema directory must contain parses/ and enums/')
    if git('status', '--porcelain'):
        parser.error('Commit source changes before generating a provenance-bearing bundle')
    inputs = {p.relative_to(schemas).as_posix(): sha(p)
              for p in sorted(schemas.rglob('*')) if p.is_file()}
    mappings = json.loads(args.mappings.read_text(encoding='utf-8'))
    identity = f'STO.Serialization.{args.build}'
    filename = identity + '.dll'
    for mapping in mappings:
        for target in mapping['targets']:
            target['versions'] = [identity]
    bundle = args.output.resolve()
    bundle.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='sto-build-', dir=ROOT) as tmp:
        stage = Path(tmp)
        logs = stage / 'logs'
        logs.mkdir()
        commands = []
        for project in ('FileFormats', 'ConvertResource', 'GenerateSerializer', 'ExportSchemas', 'Unpack'):
            command = ['dotnet', 'build', str(ROOT / 'projects' / f'Gibbed.Cryptic.{project}' /
                       f'Gibbed.Cryptic.{project}.csproj'), '-c', 'Release',
                       '-p:OutputPath=' + str(stage) + '/', '-p:NuGetAudit=false']
            commands.append(command)
            with (logs / f'build-{project}.log').open('w') as log:
                subprocess.run(command, stdout=log, stderr=subprocess.STDOUT, check=True)
            print('Built', project, flush=True)
        assemblies = stage / 'serializers/assemblies'
        assemblies.mkdir(parents=True)
        command = ['dotnet', str(stage / 'Gibbed.Cryptic.GenerateSerializer.dll'), '--game=StarTrekOnline',
                   '--path=' + str(schemas), '--version=' + args.build + '/' + args.schema_id, filename]
        commands.append(command)
        with (logs / 'generate.log').open('w') as log:
            subprocess.run(command, cwd=assemblies, stdout=log, stderr=subprocess.STDOUT, check=True)
        if not (assemblies / filename).is_file():
            raise RuntimeError('Generator returned without producing the serializer')
        if inputs != {p.relative_to(schemas).as_posix(): sha(p)
                      for p in sorted(schemas.rglob('*')) if p.is_file()}:
            raise RuntimeError('Schema inputs changed during generation')
        resources = stage / 'serializers/resources'
        resources.mkdir()
        for mapping in mappings:
            name = mapping['name']
            if not re.fullmatch(r'[A-Za-z0-9_.-]+', name):
                raise ValueError('Unsafe resource name: ' + name)
            (resources / (name + '.serializer.json')).write_text(json.dumps(mapping, indent=2) + '\n')
        outputs = {p.relative_to(stage).as_posix(): sha(p) for p in sorted(stage.rglob('*'))
                   if p.is_file() and 'logs' not in p.relative_to(stage).parts}
        manifest = {'build': args.build, 'schema_id': args.schema_id, 'source_commit': git('rev-parse', 'HEAD'),
                    'source_remote': git('remote', 'get-url', 'origin'),
                    'submodules': git('submodule', 'status', '--recursive'), 'schema_dir': str(schemas),
                    'schema_files': inputs, 'schema_fingerprint': hashlib.sha256(
                        json.dumps(inputs, sort_keys=True).encode()).hexdigest(),
                    'mapping_source': str(args.mappings.resolve()), 'mapping_source_sha256': sha(args.mappings),
                    'commands': commands, 'dotnet_sdk': subprocess.check_output(['dotnet', '--version'], text=True).strip(),
                    'serializer': filename, 'outputs': outputs}
        (stage / 'serializers/objects').mkdir()
        shutil.copytree(stage, bundle, dirs_exist_ok=True, ignore=shutil.ignore_patterns('logs'))
        evidence = bundle / 'serializers/manifests' / (identity + '.' + args.schema_id)
        evidence.mkdir(parents=True, exist_ok=True)
        shutil.copytree(logs, evidence / 'logs', dirs_exist_ok=True)
        (evidence / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        print('Generated', bundle / 'serializers/assemblies' / filename, flush=True)


if __name__ == '__main__':
    main()
