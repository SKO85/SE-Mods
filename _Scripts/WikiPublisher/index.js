'use strict';

require('dotenv').config();

const simpleGit = require('simple-git');
const inquirer = require('inquirer');
const fs = require('fs');
const path = require('path');
const os = require('os');

// ---------------------------------------------------------------------------
// Config
// ---------------------------------------------------------------------------

const PAT        = process.env.GITHUB_PAT;
const GIT_USER   = process.env.GITHUB_USER  || 'SKO85';
const GIT_EMAIL  = process.env.GITHUB_EMAIL || 'noreply@github.com';

const WIKI_REPO_URL    = `https://${GIT_USER}:${PAT}@github.com/SKO85/SE-Mods.wiki.git`;
const REPO_ROOT        = path.resolve(__dirname, '../..');
const RELEASE_NOTES_DIR = path.join(REPO_ROOT, 'SKO-Nanobot-BuildAndRepair-System', 'Docs', 'Release-Notes');

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/**
 * Maps a release note filename to its corresponding wiki page filename.
 * GitHub wiki filenames match the page title exactly.
 * The non-breaking hyphen (U+2010) in the title matches the %E2%80%90 in wiki URLs.
 *
 * Examples:
 *   release_notes.md          -> Build-and-Repair-System-Release-Notes.md
 *   release_notes_v2_4_5.md   -> Build-and-Repair-System-‐-v2.4.5.md
 */
function toWikiFileName(filename) {
  if (filename === 'release_notes.md') {
    return 'Build-and-Repair-System-Release-Notes.md';
  }

  const match = filename.match(/^release_notes_v(\d+)_(\d+)_(\d+)\.md$/);
  if (match) {
    const version = `v${match[1]}.${match[2]}.${match[3]}`;
    return `Build-and-Repair-System-\u2010-${version}.md`;
  }

  return null;
}

/**
 * Returns a human-readable label for a release note filename.
 */
function toDisplayName(filename) {
  if (filename === 'release_notes.md') return 'Release Notes Index';

  const match = filename.match(/^release_notes_v(\d+)_(\d+)_(\d+)\.md$/);
  if (match) return `v${match[1]}.${match[2]}.${match[3]}`;

  return filename;
}

/**
 * Reads and sorts release note files (index first, then versions newest-first).
 */
function loadReleaseNoteFiles() {
  const files = fs.readdirSync(RELEASE_NOTES_DIR).filter(f => f.endsWith('.md'));

  const index    = files.filter(f => f === 'release_notes.md');
  const versions = files.filter(f => f !== 'release_notes.md').sort().reverse();

  return [...index, ...versions];
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

async function main() {
  console.log('=== SKO SE-Mods Wiki Publisher ===\n');

  if (!PAT) {
    console.error('Error: GITHUB_PAT is not set.');
    console.error('Copy .env.example to .env and fill in your GitHub Personal Access Token.');
    process.exit(1);
  }

  // Load available files
  let files;
  try {
    files = loadReleaseNoteFiles();
  } catch (err) {
    console.error(`Error reading release notes from: ${RELEASE_NOTES_DIR}`);
    console.error(err.message);
    process.exit(1);
  }

  if (files.length === 0) {
    console.error('No release note files found.');
    process.exit(1);
  }

  // Ask which page(s) to publish
  const { selected } = await inquirer.prompt([
    {
      type: 'list',
      name: 'selected',
      message: 'Which release note do you want to publish to the wiki?',
      pageSize: 20,
      choices: [
        { name: `All (${files.length} pages)`, value: '__all__' },
        new inquirer.Separator(),
        ...files.map(f => ({ name: toDisplayName(f), value: f }))
      ]
    }
  ]);

  const filesToPublish = selected === '__all__' ? files : [selected];

  // Confirm
  const label = selected === '__all__'
    ? `all ${filesToPublish.length} release note pages`
    : `"${toDisplayName(selected)}"`;

  const { confirmed } = await inquirer.prompt([
    {
      type: 'confirm',
      name: 'confirmed',
      message: `Publish ${label} to the GitHub Wiki?`,
      default: true
    }
  ]);

  if (!confirmed) {
    console.log('Cancelled.');
    process.exit(0);
  }

  // Clone wiki into a temp directory
  const tmpDir = fs.mkdtempSync(path.join(os.tmpdir(), 'se-mods-wiki-'));

  try {
    console.log('\nCloning wiki repository...');
    await simpleGit().clone(WIKI_REPO_URL, tmpDir);

    const wikiGit = simpleGit(tmpDir);
    await wikiGit.addConfig('user.name', GIT_USER);
    await wikiGit.addConfig('user.email', GIT_EMAIL);

    // Copy files into the wiki repo
    console.log('');
    for (const file of filesToPublish) {
      const wikiFileName = toWikiFileName(file);
      if (!wikiFileName) {
        console.warn(`  [skip] ${file} — could not determine wiki page name.`);
        continue;
      }

      fs.copyFileSync(
        path.join(RELEASE_NOTES_DIR, file),
        path.join(tmpDir, wikiFileName)
      );
      console.log(`  [ok]   ${file}  →  ${wikiFileName}`);
    }

    // Stage all changes
    await wikiGit.add('.');
    const status = await wikiGit.status();

    if (status.isClean()) {
      console.log('\nNo changes detected — the wiki is already up to date.');
      return;
    }

    // Commit and push
    const commitMsg = filesToPublish.length === 1
      ? `Update release notes ${toDisplayName(filesToPublish[0])}`
      : `Update release notes (${filesToPublish.length} pages)`;

    await wikiGit.commit(commitMsg);
    await wikiGit.push('origin', 'master');

    console.log(`\nDone! ${filesToPublish.length} page(s) published to the wiki.`);

  } finally {
    fs.rmSync(tmpDir, { recursive: true, force: true });
  }
}

main().catch(err => {
  console.error('\nError:', err.message);
  process.exit(1);
});
