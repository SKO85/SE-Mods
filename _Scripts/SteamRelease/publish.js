#!/usr/bin/env node

var fs = require("fs");
var path = require("path");
var execSync = require("child_process").execSync;
var https = require("https");
var readline = require("readline");

var ENV_PATH = path.join(__dirname, ".env");
var CONFIG_PATH = path.join(__dirname, "config.json");
var TEMP_VDF_DIR = path.join(__dirname, ".temp");
var STEAMCMD_DIR = path.join(__dirname, "steamcmd");
var STEAMCMD_EXE = path.join(STEAMCMD_DIR, "steamcmd.exe");
var STEAMCMD_ZIP = path.join(STEAMCMD_DIR, "steamcmd.zip");
var STEAMCMD_URL = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";

function loadEnv() {
  if (!fs.existsSync(ENV_PATH)) return;
  var content = fs.readFileSync(ENV_PATH, "utf8");
  content.split("\n").forEach(function (line) {
    line = line.trim();
    if (!line || line.charAt(0) === "#") return;
    var idx = line.indexOf("=");
    if (idx === -1) return;
    var key = line.substring(0, idx).trim();
    var val = line.substring(idx + 1).trim();
    if (!process.env[key]) {
      process.env[key] = val;
    }
  });
}

function loadConfig() {
  if (!fs.existsSync(CONFIG_PATH)) {
    console.error("Error: config.json not found at " + CONFIG_PATH);
    process.exit(1);
  }
  return JSON.parse(fs.readFileSync(CONFIG_PATH, "utf8"));
}

function resolveContentFolder(config, mod) {
  if (mod.contentFolder) {
    return mod.contentFolder;
  }
  return path.join(config.modsFolder, mod.folder);
}

function resolveSteamCmd(config) {
  // If config specifies a custom path and it exists, use it
  if (config.steamcmd && config.steamcmd !== "steamcmd") {
    if (fs.existsSync(config.steamcmd)) {
      return config.steamcmd;
    }
    console.error("Configured steamcmd path not found: " + config.steamcmd);
    process.exit(1);
  }
  // Otherwise use the local copy
  return STEAMCMD_EXE;
}

function download(url, dest) {
  return new Promise(function (resolve, reject) {
    var file = fs.createWriteStream(dest);
    https.get(url, function (response) {
      if (response.statusCode === 301 || response.statusCode === 302) {
        file.close();
        fs.unlinkSync(dest);
        download(response.headers.location, dest).then(resolve).catch(reject);
        return;
      }
      response.pipe(file);
      file.on("finish", function () {
        file.close(resolve);
      });
    }).on("error", function (err) {
      fs.unlinkSync(dest);
      reject(err);
    });
  });
}

function ensureSteamCmd(config) {
  var exe = resolveSteamCmd(config);
  if (fs.existsSync(exe)) {
    return Promise.resolve(exe);
  }

  console.log("SteamCMD not found. Downloading to " + STEAMCMD_DIR + " ...");

  if (!fs.existsSync(STEAMCMD_DIR)) {
    fs.mkdirSync(STEAMCMD_DIR, { recursive: true });
  }

  return download(STEAMCMD_URL, STEAMCMD_ZIP).then(function () {
    console.log("Extracting steamcmd.zip ...");
    // Use PowerShell to extract (available on all Windows 10/11)
    execSync(
      'powershell -Command "Expand-Archive -Path \'' + STEAMCMD_ZIP + '\' -DestinationPath \'' + STEAMCMD_DIR + '\' -Force"',
      { stdio: "inherit" }
    );
    fs.unlinkSync(STEAMCMD_ZIP);

    if (!fs.existsSync(STEAMCMD_EXE)) {
      console.error("Error: steamcmd.exe not found after extraction.");
      process.exit(1);
    }

    // Run steamcmd once to let it self-update
    console.log("Running SteamCMD first-time setup (self-update) ...");
    try {
      execSync('"' + STEAMCMD_EXE + '" +quit', { stdio: "inherit" });
    } catch (e) {
      // SteamCMD often exits with non-zero on first run during update, that's OK
    }

    console.log("SteamCMD ready.");
    console.log("");
    return STEAMCMD_EXE;
  });
}

function generateVdf(config, mod, changeNote) {
  var contentPath = resolveContentFolder(config, mod).replace(/\//g, "\\");
  var lines = [
    '"workshopitem"',
    "{",
    '  "appid" "' + config.appId + '"',
    '  "publishedfileid" "' + mod.workshopId + '"',
    '  "contentfolder" "' + contentPath + '"',
  ];

  if (changeNote) {
    lines.push('  "changenote" "' + changeNote.replace(/"/g, '\\"') + '"');
  }

  lines.push("}");
  return lines.join("\n");
}

function ask(question) {
  var rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
  });

  return new Promise(function (resolve) {
    rl.question(question, function (answer) {
      rl.close();
      resolve(answer.trim());
    });
  });
}

function getArg(args, flag) {
  var idx = args.indexOf(flag);
  if (idx !== -1 && args[idx + 1]) {
    return args[idx + 1];
  }
  return null;
}

function selectMod(mods) {
  console.log("Select a mod to publish:");
  mods.forEach(function (mod, i) {
    console.log("  " + (i + 1) + ") " + mod.name + " (Workshop ID: " + mod.workshopId + ")");
  });
  console.log("");

  return ask("Enter number (1-" + mods.length + "): ").then(function (answer) {
    var idx = parseInt(answer, 10) - 1;
    if (isNaN(idx) || idx < 0 || idx >= mods.length) {
      console.error("Invalid selection.");
      process.exit(1);
    }
    return [mods[idx]];
  });
}

async function loginSteamCmd() {
  loadEnv();
  var config = loadConfig();

  if (!process.env.STEAM_USERNAME) {
    console.error("Error: STEAM_USERNAME is not set.");
    console.error("  Add it to .env or set it as an environment variable.");
    process.exit(1);
  }

  var steamcmdExe = await ensureSteamCmd(config);
  console.log("Logging in to Steam as " + process.env.STEAM_USERNAME + " ...");
  console.log("SteamCMD will prompt for your password and Steam Guard code.");
  console.log("After successful login, the session is cached for future runs.");
  console.log("");

  try {
    execSync('"' + steamcmdExe + '" +login "' + process.env.STEAM_USERNAME + '" +quit', { stdio: "inherit" });
    console.log("");
    console.log("Login successful. Session cached in steamcmd/ folder.");
  } catch (err) {
    console.error("Login failed: " + err.message);
    process.exit(1);
  }
}

async function main() {
  var args = process.argv.slice(2);

  // Handle --login separately
  if (args.indexOf("--login") !== -1) {
    return loginSteamCmd();
  }

  var confirmed = args.indexOf("--confirm") !== -1;
  var dryRun = !confirmed;
  var publishAll = args.indexOf("--all") !== -1;
  var modName = getArg(args, "--name");

  loadEnv();
  var config = loadConfig();
  var enabledMods = config.mods.filter(function (m) { return m.enabled; });

  if (enabledMods.length === 0) {
    console.error("No enabled mods found in config.json.");
    console.error("\nAvailable mods:");
    config.mods.forEach(function (m) {
      console.error("  - " + m.name + " (enabled: " + m.enabled + ")");
    });
    process.exit(1);
  }

  // Determine which mods to publish
  var modsToPublish;

  if (modName) {
    // --name <name>: pick a specific enabled mod
    var match = enabledMods.filter(function (m) {
      return m.name.toLowerCase() === modName.toLowerCase();
    });
    if (match.length === 0) {
      var exists = config.mods.some(function (m) {
        return m.name.toLowerCase() === modName.toLowerCase();
      });
      if (exists) {
        console.error('"' + modName + '" is disabled in config.json. Set enabled: true to allow publishing.');
      } else {
        console.error('No mod found with name "' + modName + '".');
      }
      console.error("\nEnabled mods:");
      enabledMods.forEach(function (m) {
        console.error("  - " + m.name);
      });
      process.exit(1);
    }
    modsToPublish = match;
  } else if (publishAll) {
    // --all: publish all enabled mods
    modsToPublish = enabledMods;
  } else {
    // No flag: interactive selection from enabled mods
    if (enabledMods.length === 1) {
      modsToPublish = enabledMods;
    } else {
      modsToPublish = await selectMod(enabledMods);
    }
  }

  console.log("=== Steam Workshop Publisher ===");
  console.log("Mode: " + (dryRun ? "DRY RUN" : "LIVE"));
  console.log("");

  // Build the mod if configured
  if (config.build) {
    var needsBuild = modsToPublish.some(function (m) { return !m.contentFolder; });
    if (needsBuild) {
      console.log("Building mod...");
      try {
        execSync(config.build.command, {
          cwd: config.build.workingDir,
          stdio: "inherit"
        });
      } catch (err) {
        console.error("Build failed: " + err.message);
        process.exit(1);
      }
      var delay = (config.build.delaySeconds || 3) * 1000;
      console.log("Waiting " + (delay / 1000) + "s for build to settle...");
      await new Promise(function (resolve) { setTimeout(resolve, delay); });
      console.log("");
    }
  }

  // Ensure SteamCMD is available (download if needed, skip for dry-run)
  var steamcmdExe;
  if (!dryRun) {
    steamcmdExe = await ensureSteamCmd(config);
  }

  // Validate content folders exist
  var valid = true;
  modsToPublish.forEach(function (mod) {
    var folder = resolveContentFolder(config, mod);
    if (!fs.existsSync(folder)) {
      console.error("[" + mod.name + "] Content folder not found: " + folder);
      console.error("  Run a build first to populate the mod folder.");
      valid = false;
    } else {
      console.log("[" + mod.name + "] Content folder OK: " + folder);
    }
  });

  if (!valid) {
    process.exit(1);
  }

  console.log("");

  // Ask for change note (skip in confirmed mode unless --note is provided)
  var changeNote = getArg(args, "--note") || getArg(args, "--notes") || "";
  if (!confirmed && !changeNote) {
    changeNote = await ask("Change note (optional, press Enter to skip): ");
  }

  console.log("The following mods will be published:");
  modsToPublish.forEach(function (mod) {
    console.log("  - " + mod.name + " (Workshop ID: " + mod.workshopId + ")");
  });
  if (changeNote) {
    console.log("  Change note: " + changeNote);
  }
  console.log("");

  // Ensure STEAM_USERNAME is set for live runs
  if (!dryRun && !process.env.STEAM_USERNAME) {
    console.error("Error: STEAM_USERNAME environment variable is not set.");
    console.error("  Set it before running: set STEAM_USERNAME=your_steam_username");
    console.error("  SteamCMD will prompt for password/2FA interactively.");
    process.exit(1);
  }

  // Create temp dir for VDF files
  if (!fs.existsSync(TEMP_VDF_DIR)) {
    fs.mkdirSync(TEMP_VDF_DIR, { recursive: true });
  }

  var failed = false;

  for (var i = 0; i < modsToPublish.length; i++) {
    var mod = modsToPublish[i];
    var vdfContent = generateVdf(config, mod, changeNote);
    var vdfPath = path.join(TEMP_VDF_DIR, mod.name.toLowerCase() + ".vdf");

    fs.writeFileSync(vdfPath, vdfContent, "utf8");

    console.log("");
    console.log("[" + mod.name + "] Generated VDF:");
    console.log(vdfContent);
    console.log("");

    if (dryRun) {
      console.log("[" + mod.name + "] Dry run - skipping upload.");
      continue;
    }

    console.log("[" + mod.name + "] Uploading to Steam Workshop...");

    var cmd = '"' + steamcmdExe + '" +login "' + process.env.STEAM_USERNAME + '" +workshop_build_item "' + vdfPath + '" +quit';

    try {
      execSync(cmd, { stdio: "inherit" });
      console.log("[" + mod.name + "] Upload complete.");
    } catch (err) {
      console.error("[" + mod.name + "] Upload failed: " + err.message);
      failed = true;
    }
  }

  // Cleanup temp VDF files
  if (fs.existsSync(TEMP_VDF_DIR)) {
    fs.readdirSync(TEMP_VDF_DIR).forEach(function (file) {
      fs.unlinkSync(path.join(TEMP_VDF_DIR, file));
    });
    fs.rmdirSync(TEMP_VDF_DIR);
  }

  console.log("");
  if (failed) {
    console.log("Completed with errors.");
    process.exit(1);
  } else {
    console.log("Done.");
  }
}

main().catch(function (err) {
  console.error("Fatal error: " + err.message);
  process.exit(1);
});
