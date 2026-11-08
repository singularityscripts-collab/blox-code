#!/usr/bin/env node
// Publishes Blox Code updates without depending on any one website.
//
//   node tools/release.js keygen
//       Creates the release-signing key (once). The private key stays in your user folder
//       (~/.bloxcode-release/update-signing-key.pem, or $BLOXCODE_SIGNING_KEY); the public key is
//       written into Directory.Build.props so every build trusts it. BACK THE PRIVATE KEY UP: without
//       it, installed copies can't be updated again.
//
//   node tools/release.js feed --exe dist/BloxCode.exe --appimage dist/BloxCode-x86_64.AppImage
//                               [--out dist/feed] [--mirror "https://host/path/v{version}/{name}"]...
//       Builds the feed folder for the version in Directory.Build.props:
//         latest.json        version, title, notes from CHANGELOG.md, and per platform the file's
//                            size, SHA-256 and download addresses
//         latest.json.sig    ECDSA P-256 / SHA-256 signature of latest.json (base64, r||s)
//         <version>/...      the files themselves
//         latest/...         the same files at a fixed address, for download links
//       Upload the whole folder to any static host (keep the files next to latest.json) and put
//       that folder's address in <UpdateFeeds>. --mirror adds extra download addresses, tried after
//       the copy next to the manifest; {version} and {name} are filled in.
//
//   node tools/release.js fetch-github --github owner/repo
//       Downloads BloxCode.exe and BloxCode-x86_64.AppImage from the GitHub release v<version>
//       (built by GitHub Actions) into dist/release/v<version>/, for the feed step.
//
//   node tools/release.js feed --github owner/repo --exe ... --appimage ...
//       The GitHub layout: latest.json + .sig go to updates/ in this repository (served from
//       raw.githubusercontent.com and from every mirror of the repository), and the files are
//       downloaded from https://github.com/owner/repo/releases/download/v<version>/<name>.
//
//   node tools/release.js feed --gitlab group/project --exe ... --appimage ...
//       The GitLab layout: latest.json + .sig go to updates/ in this repository (the feed is
//       https://gitlab.com/group/project/-/raw/main/updates/), and the files are downloaded from the
//       project's generic package registry (bloxcode/<version>/<name>). The files are copied to
//       dist/release/v<version>/ for the next step.
//
//   node tools/release.js publish-gitlab --gitlab group/project
//       Uploads dist/release/v<version>/ to the package registry and creates the GitLab release
//       v<version> with download links. Asks for a GitLab access token (scope "api") without
//       showing it, or reads $GITLAB_TOKEN; the token is never written anywhere. Run it before
//       pushing updates/, so installed copies never see a manifest whose files aren't online yet.
//
//   node tools/release.js verify <feed folder or https:// address>
//       Checks the signature with the key in Directory.Build.props, and the files' hashes when they
//       are local or that they download when they are remote.
'use strict';

const crypto = require('crypto');
const fs = require('fs');
const os = require('os');
const path = require('path');

const root = path.resolve(__dirname, '..');
const propsFile = path.join(root, 'Directory.Build.props');
const changelogFile = path.join(root, 'CHANGELOG.md');
const keyFile = process.env.BLOXCODE_SIGNING_KEY || path.join(os.homedir(), '.bloxcode-release', 'update-signing-key.pem');
const platforms = {
  'windows-x64': { option: 'exe', name: 'BloxCode.exe' },
  'linux-x64': { option: 'appimage', name: 'BloxCode-x86_64.AppImage' },
};

function fail(message) {
  console.error(`error: ${message}`);
  process.exit(1);
}

function readProps() {
  return fs.readFileSync(propsFile, 'utf8');
}

function prop(name) {
  const match = readProps().match(new RegExp(`<${name}>([^<]*)</${name}>`));
  return match ? match[1].trim() : '';
}

function setProp(name, value) {
  const props = readProps();
  const pattern = new RegExp(`<${name}>[^<]*</${name}>`);
  if (!pattern.test(props)) fail(`Directory.Build.props has no <${name}>`);
  fs.writeFileSync(propsFile, props.replace(pattern, `<${name}>${value}</${name}>`));
}

function args(list) {
  const result = { _: [] };
  for (let i = 0; i < list.length; i++) {
    const a = list[i];
    if (!a.startsWith('--')) { result._.push(a); continue; }
    const key = a.slice(2);
    const value = list[i + 1] && !list[i + 1].startsWith('--') ? list[++i] : true;
    if (key === 'mirror') (result.mirror ||= []).push(value);
    else result[key] = value;
  }
  return result;
}

function publicKeyBase64(privateKey) {
  return crypto.createPublicKey(privateKey).export({ type: 'spki', format: 'der' }).toString('base64');
}

function loadPrivateKey() {
  if (!fs.existsSync(keyFile)) fail(`no signing key at ${keyFile}; run "node tools/release.js keygen" first`);
  return crypto.createPrivateKey(fs.readFileSync(keyFile));
}

function sign(data, privateKey) {
  return crypto.sign('sha256', data, { key: privateKey, dsaEncoding: 'ieee-p1363' }).toString('base64');
}

function verify(data, signatureText, publicKey) {
  const key = crypto.createPublicKey({ key: Buffer.from(publicKey, 'base64'), format: 'der', type: 'spki' });
  return crypto.verify('sha256', data, { key, dsaEncoding: 'ieee-p1363' }, Buffer.from(signatureText.trim(), 'base64'));
}

function sha256File(file) {
  return crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
}

function changelogSection(version) {
  const lines = fs.readFileSync(changelogFile, 'utf8').replace(/\r\n/g, '\n').split('\n');
  const start = lines.findIndex(l => new RegExp(`^## ${version.replace(/\./g, '\\.')}( |$)`).test(l));
  if (start < 0) return null;
  const title = lines[start].replace(/^## \S+\s*(—\s*)?/, '').trim();
  const body = [];
  for (const line of lines.slice(start + 1)) {
    if (line.startsWith('## ')) break;
    body.push(line);
  }
  return { title, notes: body.join('\n').trim() };
}

function keygen(options) {
  if (fs.existsSync(keyFile) && !options.force) {
    console.log(`A signing key already exists at ${keyFile}; keeping it.`);
  } else {
    const { privateKey } = crypto.generateKeyPairSync('ec', { namedCurve: 'P-256' });
    fs.mkdirSync(path.dirname(keyFile), { recursive: true });
    fs.writeFileSync(keyFile, privateKey.export({ type: 'pkcs8', format: 'pem' }), { mode: 0o600 });
    console.log(`Created the signing key at ${keyFile}`);
  }

  const publicKey = publicKeyBase64(loadPrivateKey());
  const current = prop('UpdatePublicKey');
  if (current && current !== publicKey && !options.force)
    fail('Directory.Build.props already trusts a different key. Installed copies only accept that one; pass --force only if you really mean to replace it.');
  setProp('UpdatePublicKey', publicKey);
  console.log(`Directory.Build.props now trusts: ${publicKey}`);
  console.log('Back the private key up somewhere safe (a password manager or an offline drive). Never commit it.');
}

function feed(options) {
  const version = prop('Version');
  if (!/^\d+\.\d+\.\d+$/.test(version)) fail(`unexpected <Version> "${version}"`);
  const section = changelogSection(version);
  if (!section) fail(`CHANGELOG.md has no "## ${version}" section`);

  const privateKey = loadPrivateKey();
  const trusted = prop('UpdatePublicKey');
  if (publicKeyBase64(privateKey) !== trusted)
    fail(`the key at ${keyFile} isn't the one Directory.Build.props trusts; builds would reject this feed`);

  // GitHub / GitLab: the manifest lives in the repository, the files on the release (GitHub) or in
  // the package registry (GitLab).
  const gitlab = gitlabProject(options);
  const github = githubRepo(options);
  const hosted = Boolean(gitlab || github);
  const out = path.resolve(options.out || (hosted ? path.join(root, 'updates') : path.join(root, 'dist', 'feed')));
  const filesDir = hosted ? releaseDir(version) : path.join(out, version);
  fs.mkdirSync(filesDir, { recursive: true });
  fs.mkdirSync(out, { recursive: true });
  const mirrors = [
    ...(github ? [`https://github.com/${github}/releases/download/v{version}/{name}`] : []),
    ...(gitlab ? [packageUrl(gitlab, '{version}', '{name}')] : []),
    ...(options.mirror || []),
  ];

  const assets = {};
  for (const [platform, { option, name }] of Object.entries(platforms)) {
    const source = options[option];
    if (!source) {
      console.warn(`warning: no --${option}; ${platform} copies won't see this release`);
      continue;
    }
    if (!fs.existsSync(source)) fail(`${source} doesn't exist`);
    const target = path.join(filesDir, name);
    if (path.resolve(source) !== path.resolve(target))
      fs.copyFileSync(source, target);
    if (!hosted) {
      // A fixed address for download links; the app itself uses the versioned copy.
      fs.mkdirSync(path.join(out, 'latest'), { recursive: true });
      fs.copyFileSync(source, path.join(out, 'latest', name));
    }
    const urls = [
      ...(hosted ? [] : [`${version}/${name}`]),
      ...mirrors.map(m => m.replace('{version}', version).replace('{name}', name)),
    ];
    assets[platform] = { name, size: fs.statSync(target).size, sha256: sha256File(target), urls };
  }
  if (Object.keys(assets).length === 0) fail('pass --exe and/or --appimage');

  const manifest = {
    format: 1,
    product: 'BloxCode',
    version,
    title: section.title,
    published: new Date().toISOString(),
    notes: section.notes,
    assets,
  };
  const data = Buffer.from(JSON.stringify(manifest, null, 2) + '\n', 'utf8');
  fs.writeFileSync(path.join(out, 'latest.json'), data);
  fs.writeFileSync(path.join(out, 'latest.json.sig'), sign(data, privateKey) + '\n');

  if (!verify(data, fs.readFileSync(path.join(out, 'latest.json.sig'), 'utf8'), trusted)) fail('the new signature does not verify');
  console.log(`Feed for ${version} written to ${out}`);
  for (const [platform, asset] of Object.entries(assets))
    console.log(`  ${platform}: ${asset.name}, ${asset.size} bytes, sha256 ${asset.sha256}`);
  if (github) {
    console.log(`Next: commit updates/ and push it to GitHub and GitLab — that push is what installed copies see.`);
  } else if (gitlab) {
    console.log(`Next: node tools/release.js publish-gitlab --gitlab ${gitlab}  (uploads ${filesDir} and creates the release),`);
    console.log(`then commit and push ${path.relative(root, out)}/ — that push is what installed copies see.`);
  } else {
    console.log('Upload the whole folder (keeping its layout) to each feed address in <UpdateFeeds>.');
  }
}

async function verifyFeed(target) {
  if (!target) fail('pass a feed folder or address');
  const trusted = prop('UpdatePublicKey');
  if (!trusted) fail('Directory.Build.props has no <UpdatePublicKey>');

  const remote = /^https?:\/\//.test(target);
  const base = remote ? target.replace(/\/?(latest\.json)?$/, '/') : path.resolve(target);
  const read = async name => {
    if (!remote) return fs.readFileSync(path.join(base, name));
    const response = await fetch(base + name, { headers: { 'cache-control': 'no-cache' } });
    if (!response.ok) fail(`${base + name}: HTTP ${response.status}`);
    return Buffer.from(await response.arrayBuffer());
  };

  const data = await read('latest.json');
  const signature = (await read('latest.json.sig')).toString('utf8');
  if (!verify(data, signature, trusted)) fail('the signature does NOT match the trusted key');
  const manifest = JSON.parse(data.toString('utf8'));
  console.log(`Signature OK: Blox Code ${manifest.version} — ${manifest.title}`);

  for (const [platform, asset] of Object.entries(manifest.assets)) {
    if (remote || /^https?:\/\//.test(asset.urls[0])) {
      // Remote files: check they're reachable and the right size.
      for (const url of asset.urls.map(u => new URL(u, remote ? base + 'latest.json' : 'file:///').href)) {
        // GET (not HEAD, which some registries refuse), stopping after the headers.
        const response = url.startsWith('http') ? await fetch(url, { redirect: 'follow' }).catch(e => ({ ok: false, status: e.message })) : { ok: false, status: 'not http' };
        const size = response.ok ? Number(response.headers.get('content-length')) : 0;
        await response.body?.cancel?.().catch(() => {});
        const ok = response.ok && (!size || size === asset.size);
        console.log(`  ${platform}: ${url} ${ok ? 'reachable' : `NOT OK (${response.status}${size ? `, ${size} bytes` : ''})`}`);
        if (!ok) process.exitCode = 1;
      }
      continue;
    }
    const file = path.join(base, ...asset.urls[0].split('/'));
    const ok = fs.existsSync(file) && sha256File(file) === asset.sha256;
    console.log(`  ${platform}: ${asset.name} ${ok ? 'hash OK' : 'MISSING OR WRONG HASH'}`);
    if (!ok) process.exitCode = 1;
  }
}

function gitlabProject(options) {
  if (!options.gitlab) return null;
  const project = String(options.gitlab).replace(/^https:\/\/gitlab\.com\//, '').replace(/(\.git)?\/?$/, '');
  if (!/^[\w.-]+(\/[\w.-]+)+$/.test(project)) fail('--gitlab expects group/project');
  return project;
}

function githubRepo(options) {
  if (!options.github) return null;
  const repo = String(options.github).replace(/^https:\/\/github\.com\//, '').replace(/(\.git)?\/?$/, '');
  if (!/^[\w.-]+\/[\w.-]+$/.test(repo)) fail('--github expects owner/repo');
  return repo;
}

/** Downloads this version's files from the GitHub release (public, no login needed). */
async function fetchGithub(options) {
  const repo = githubRepo(options) || fail('pass --github owner/repo');
  const version = prop('Version');
  const dir = releaseDir(version);
  fs.mkdirSync(dir, { recursive: true });
  for (const { name } of Object.values(platforms)) {
    const url = `https://github.com/${repo}/releases/download/v${version}/${name}`;
    process.stdout.write(`Downloading ${name}… `);
    const response = await fetch(url, { redirect: 'follow' });
    if (!response.ok) { console.log(`HTTP ${response.status}`); process.exitCode = 1; continue; }
    const data = Buffer.from(await response.arrayBuffer());
    fs.writeFileSync(path.join(dir, name), data);
    console.log(`${(data.length / 1048576).toFixed(1)} MB`);
  }
  console.log(`Saved to ${dir}`);
}

function releaseDir(version) {
  return path.join(root, 'dist', 'release', `v${version}`);
}

const gitlabApi = project => `https://gitlab.com/api/v4/projects/${encodeURIComponent(project)}`;
const packageUrl = (project, version, name) => `${gitlabApi(project)}/packages/generic/bloxcode/${version}/${name}`;

/** Reads the token from $GITLAB_TOKEN or the keyboard, never echoing it. */
function askToken() {
  if (process.env.GITLAB_TOKEN) return Promise.resolve(process.env.GITLAB_TOKEN.trim());
  if (!process.stdin.isTTY) fail('set GITLAB_TOKEN or run this in a terminal');
  process.stdout.write('GitLab access token (scope "api"; nothing shows while you paste): ');
  return new Promise(resolve => {
    let token = '';
    process.stdin.setRawMode(true);
    process.stdin.resume();
    process.stdin.setEncoding('utf8');
    process.stdin.on('data', function onData(chunk) {
      for (const ch of chunk) {
        if (ch === '\r' || ch === '\n') {
          process.stdin.setRawMode(false);
          process.stdin.pause();
          process.stdin.removeListener('data', onData);
          process.stdout.write('\n');
          return resolve(token.trim());
        }
        if (ch === '\u0003') process.exit(130); // Ctrl+C
        if (ch === '\u0008' || ch === '\u007f') token = token.slice(0, -1);
        else token += ch;
      }
    });
  });
}

async function publishGitlab(options) {
  const project = gitlabProject(options) || fail('pass --gitlab group/project');
  const version = prop('Version');
  const dir = releaseDir(version);
  const files = fs.existsSync(dir) ? fs.readdirSync(dir).filter(n => Object.values(platforms).some(p => p.name === n)) : [];
  if (files.length === 0) fail(`nothing in ${dir}; run "feed --gitlab ${project}" first`);
  const section = changelogSection(version) || fail(`CHANGELOG.md has no "## ${version}" section`);

  const token = await askToken();
  if (!token) fail('no token given');
  const headers = { 'PRIVATE-TOKEN': token };

  for (const name of files) {
    const file = path.join(dir, name);
    process.stdout.write(`Uploading ${name} (${(fs.statSync(file).size / 1048576).toFixed(1)} MB)… `);
    const response = await fetch(packageUrl(project, version, name), { method: 'PUT', headers, body: fs.readFileSync(file) });
    if (!response.ok) fail(`upload of ${name} failed: HTTP ${response.status} ${await response.text()}`);
    console.log('done');
  }

  const links = files.map(name => ({ name, url: packageUrl(project, version, name), link_type: 'package' }));
  const description = [
    '### Baixar',
    '',
    ...links.map(l => `- [${l.name}](${l.url})`),
    '',
    'Quem já tem o Blox Code 1.6.0 ou mais novo recebe esta versão sozinho, com atualização assinada.',
    '',
    '### O que mudou',
    '',
    section.notes,
  ].join('\n');
  const response = await fetch(`${gitlabApi(project)}/releases`, {
    method: 'POST',
    headers: { ...headers, 'content-type': 'application/json' },
    body: JSON.stringify({ tag_name: `v${version}`, ref: 'main', name: `v${version} — ${section.title}`, description, assets: { links } }),
  });
  if (response.status === 409) console.log(`Release v${version} already exists; the files were updated.`);
  else if (!response.ok) fail(`creating the release failed: HTTP ${response.status} ${await response.text()}`);
  else console.log(`Release v${version} created: https://gitlab.com/${project}/-/releases/v${version}`);
  console.log('Now commit and push updates/ to make installed copies see it.');
}

const [command, ...rest] = process.argv.slice(2);
const options = args(rest);
switch (command) {
  case 'keygen': keygen(options); break;
  case 'feed': feed(options); break;
  case 'publish-gitlab': publishGitlab(options); break;
  case 'fetch-github': fetchGithub(options); break;
  case 'verify': verifyFeed(options._[0]); break;
  default:
    console.log('usage: node tools/release.js keygen | fetch-github --github owner/repo | feed [--github owner/repo | --gitlab group/project] --exe <file> --appimage <file> [--out <dir>] [--mirror <url>] | publish-gitlab --gitlab group/project | verify <folder|url>');
    process.exit(command ? 1 : 0);
}
