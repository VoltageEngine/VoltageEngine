import React, {useEffect, useState} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Layout from '@theme/Layout';
import styles from './download.module.css';

const repo = 'VoltageEngine/VoltageEngine';
const latest = `https://github.com/${repo}/releases/latest`;
const asset = (name) => `${latest}/download/${name}`;

const platforms = [
  {
    id: 'windows',
    name: 'Windows',
    detail: 'Windows 10 or 11, 64-bit',
    files: [{label: 'Download for Windows', file: 'Voltage-Editor-win-x64.zip', note: 'x64 · .zip'}],
    firstRun: 'Unzip the folder and run Voltage.Editor.exe.',
  },
  {
    id: 'mac',
    name: 'macOS',
    detail: 'macOS 12 or newer',
    files: [
      {label: 'Download for Apple Silicon', file: 'Voltage-Editor-osx-arm64.dmg', note: 'M1 and newer · .dmg'},
      {label: 'Download for Intel Mac', file: 'Voltage-Editor-osx-x64.dmg', note: 'Intel · .dmg'},
    ],
    firstRun: 'Open the disk image and drag Voltage Editor into Applications.',
  },
  {
    id: 'linux',
    name: 'Linux',
    detail: 'x64, glibc 2.31 or newer',
    files: [{label: 'Download for Linux', file: 'Voltage-Editor-linux-x64.tar.gz', note: 'x64 · .tar.gz'}],
    firstRun: 'Extract with tar -xzf, then run ./Voltage.Editor. You need an OpenGL 3 driver (Mesa or your GPU vendor\'s).',
  },
];

function detectPlatform() {
  if (typeof navigator === 'undefined') return null;
  const ua = `${navigator.userAgentData?.platform ?? ''} ${navigator.platform ?? ''} ${navigator.userAgent ?? ''}`.toLowerCase();
  if (ua.includes('win')) return 'windows';
  if (ua.includes('mac')) return 'mac';
  if (ua.includes('linux') && !ua.includes('android')) return 'linux';
  return null;
}

function useLatestRelease() {
  const [release, setRelease] = useState(null);
  useEffect(() => {
    let cancelled = false;
    fetch(`https://api.github.com/repos/${repo}/releases/latest`, {headers: {Accept: 'application/vnd.github+json'}})
      .then((r) => (r.ok ? r.json() : null))
      .then((data) => {
        if (!cancelled && data?.tag_name) {
          setRelease({tag: data.tag_name, date: data.published_at?.slice(0, 10), url: data.html_url});
        }
      })
      .catch(() => {});
    return () => {
      cancelled = true;
    };
  }, []);
  return release;
}

function PlatformCard({platform, recommended}) {
  return (
    <div className={clsx('col col--4', styles.column)}>
      <div className={clsx(styles.card, recommended && styles.recommended)}>
        <div className={styles.cardHeader}>
          <h2>{platform.name}</h2>
          {recommended && <span className={styles.badge}>Your system</span>}
        </div>
        <p className={styles.detail}>{platform.detail}</p>
        <div className={styles.files}>
          {platform.files.map((f) => (
            <a key={f.file} className={clsx('button button--lg', recommended ? 'button--primary' : 'button--secondary')} href={asset(f.file)}>
              {f.label}
              <small>{f.note}</small>
            </a>
          ))}
        </div>
        <p className={styles.firstRun}>{platform.firstRun}</p>
      </div>
    </div>
  );
}

export default function Download() {
  const [current, setCurrent] = useState(null);
  const release = useLatestRelease();
  useEffect(() => setCurrent(detectPlatform()), []);

  return (
    <Layout title="Download" description="Download the Voltage Editor for Windows, macOS and Linux.">
      <header className={clsx('hero', styles.hero)}>
        <div className="container">
          <h1 className={styles.title}>Download Voltage</h1>
          <p className={styles.subtitle}>
            The editor and runtime in one self-contained download. No .NET install needed to run it.
          </p>
          <p className={styles.version}>
            {release ? (
              <>
                Latest release <Link to={release.url}>{release.tag}</Link>
                {release.date && <> · {release.date}</>}
              </>
            ) : (
              <Link to={latest}>Latest release on GitHub</Link>
            )}
          </p>
        </div>
      </header>
      <main className="container">
        <section className={clsx('row', styles.platforms)}>
          {platforms.map((p) => (
            <PlatformCard key={p.id} platform={p} recommended={p.id === current} />
          ))}
        </section>

        <section className={styles.notes}>
          <h2>Making games</h2>
          <p>
            The editor builds your game's scripts on its own. To publish a game as a NativeAOT binary you also need the{' '}
            <Link to="/docs/getting-started/installation">native toolchain for the target platform</Link>.
          </p>

          <h2>Older versions and source</h2>
          <p>
            Every version is on the <Link to={`https://github.com/${repo}/releases`}>GitHub releases page</Link>. To work on the engine itself,{' '}
            <Link to="/docs/getting-started/installation">build the editor from source</Link>.
          </p>
        </section>
      </main>
    </Layout>
  );
}
