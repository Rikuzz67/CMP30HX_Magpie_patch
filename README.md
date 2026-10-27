# CMP 30HX · Magpie Community Edition DLSSNR One-Click Compatibility Patch

**v2 update:** The installer now applies the 30HX compatibility patch and both performance changes in one operation on the exact **SAOG0721/Magpie 0.6.11 community release**. Version 0.6.10 retains its original compatibility patch. This is a patch package, not a replacement Magpie distribution. Use the existing Install / Check / Restore CMD files; no Python is needed.

Existing v1 users: exit Magpie, run this package's **Restore-30HX.cmd**, then **Install-30HX.cmd**. The old backup is retained as an archive. The earlier standalone V2 application folder has a separate two-layer backup layout; use its own restoration tools or apply this package to a fresh community release directory.

The 0.6.11 optimization requires the community **310.9.16** model. It adds a precomputed-weight fast path for exact 4:1 downsampling and sets maximum present queue depth to 1 on the affected swap-chain path. The measured downsample saving is about **0.009 ms**, not a 6.5% improvement to the whole pipeline. Full-size input does not use the downsample fast path. Model inference remains the main cost; 1080p 60 FPS, full-load end-to-end latency, and full DLSS5 equivalence have **not** been demonstrated. Styles, intensity, input scale, FPS settings and model weights are unchanged. The present queue change also affects other effects using the same path; the separate DirectComposition path is unaffected.

The EXE is patched at exact, hash-locked locations; this package does not rebuild the entire Magpie application. Auditable C# code, the embedded shader, and an upstream source diff are included under `src/`.

The source diff targets upstream commit `4fc74a196993c883ce2f01e96e8ed8d81f1490e6`. The downsample tests found at most one 8-bit channel level of difference between the original and optimized filter. These filter results do not establish pixel-identical neural output. At 480×270, the unchanged model still took about 80 ms per inference in the standalone test. This update does not claim to fix slow initialization.

The unified installer was tested on isolated copies under Windows PowerShell 5.1 for installation, integrity checks, full restoration, reinstallation, duplicate-install rejection, tampering rejection, configuration preservation, and migration from the compatibility-only patch. See `VALIDATION-v2.json`; the full application pipeline was not rerun as part of installer validation.

This project provides an automated compatibility patch and dedicated loader for the **NVIDIA CMP 30HX** (TU116 architecture, lacking physical Tensor Cores), enabling it to successfully initialize and run the DLSSNR (Feature 18) image filter within the [SAOG0721/Magpie](https://github.com/SAOG0721/Magpie) community edition.

> **Important Note**: This project exclusively targets the **DLSSNR / Feature 18 image filter path** within this software package. Existing verification does not signify that the full DLSS5 feature set (such as independent super resolution, ray reconstruction, or all frame generation capabilities) has been implemented on the CMP 30HX.

---

## 1. Technical Implementation Principles

The CMP 30HX lacks physical Tensor Cores at the hardware level and has no official display output. Calling DLSS-related components under default conditions encounters multi-layered software and hardware verification blocks:

1. **Bypassing hardware whitelist interception**:
   * During the initialization phase, the original `_nvngx.dll` checks the GPU architecture and directly returns `0xbad00001` (`unsupported hw detected`) when unsupported hardware is detected.
   * This solution bypasses the hardware interception logic by locating and patching 3 conditional jump branches in the core binary (modifying `0F 85` to a forced jump `E9`, while preserving the original jump target).
   * This process neither adds physical processing units nor retrains the model.
2. **Bypassing Magpie signature verification**:
   * Because the hash is altered, the modified `_nvngx.dll` is rejected by Magpie's native loader with `0x80096010` (`TRUST_E_BAD_DIGEST`) and falls back to the system driver, triggering the error again.
   * This tool redirects the local NGX load call to the accompanying `30HXCoreLoader.dll` by injecting a new `.hxload` section into `Magpie.exe`.
3. **Fixed path and SHA256 locking**:
   * The `30HXCoreLoader.dll` uses the Windows BCrypt cryptography API to calculate and strictly verify the SHA256 digest of the local `_nvngx.dll`.
   * `LoadLibraryExW` is called only when it perfectly matches a known compatible hash, which lifts system signature restrictions and eliminates the injection risk of any unknown DLL.
4. **Pipeline integration and output**:
   * It retains upstream Magpie's existing snippet direct loading logic and module camouflage mechanism, completing Feature 18 creation, Evaluate execution, and GPU Fence synchronized presentation within the D3D12 pipeline.

---

## 2. Supported Versions & Verification Status

This tool incorporates strict fingerprint verification, supporting only the known versions listed in `versions.json`. **It does not support the official Blinue release, nor does it blindly match future unknown versions**.

| Magpie Version | Adaptation Status | Corresponding Download | Description |
| :--- | :--- | :--- | :--- |
| **v0.6.11** (Experimental) | Passed isolated D3D12 tests | [0.6.11 Release Page](https://github.com/SAOG0721/Magpie/releases/tag/v0.6.11-experimental) · [Full ZIP](https://github.com/SAOG0721/Magpie/releases/download/v0.6.11-experimental/Magpie-Experimental-x64.zip) | New version adaptation, built-in 310.9.16 model passed independent verification |
| **v0.6.10** (Stable) | Passed full in-app tests | [0.6.10 Release Page](https://github.com/SAOG0721/Magpie/releases/tag/v0.6.10-experimental) · [Full ZIP](https://github.com/SAOG0721/Magpie/releases/download/v0.6.10-experimental/Magpie-Experimental-x64.zip) | Verified to run consistently and stably, supporting smooth switching between three styles |

### Known File Fingerprints (SHA256)

* **NVIDIA Driver Core** (Verified Driver 616.92):
  * Original `_nvngx.dll`: `91d3742f0df3f2dd85141fa39ea257f7b9243c3e6755cd7fea894c721e7491aa`
  * Patched `_nvngx.dll`: `cf36298fb8bdcf46e0e5d2f558f9132f9aff86a95db5ce5ab849fbacb4083702`
* **Recognized Community Models**:
  * `310.8.2.0`: `6eb209e764f39872625debd6abaf45e2bb6322f6f270f781f70c059ae30b3927`
  * `310.9.16.0`: `a8a237a967469c455a84654932b513ab656fbaa1e0c44504b1e1bf0890f902b8`

---

## 3. Usage Tutorial

The patch package includes fully automated scripts, requiring **no Python, Visual Studio configuration, or manual compilation**, running directly via native PowerShell 5.1.

### Step 1: Software Preparation
1. Download the corresponding community edition ZIP (e.g., `v0.6.11-experimental`) and extract it to a new directory (e.g., `D:\Magpie-30HX\`).
2. Ensure any currently running instance of Magpie is completely exited.

### Step 2: One-Click Patch Installation
1. Extract this patch package and double-click **`Install-30HX.cmd`** to run.
2. In the popup file picker, locate and select `Magpie.exe` in the newly extracted directory.
3. The installation script will automatically execute the following operations:
   * Automatically search the system DriverStore in read-only mode for the supported `_nvngx.dll` core (if not found, the script will prompt you to manually specify a backup core; it will absolutely not modify system drivers).
   * Generate a dedicated compatible core copy locally in the application folder.
   * Inject the `.hxload` section and deploy `30HXCoreLoader.dll`.
   * Verify the community DLSSNR model fingerprint.
   * On 0.6.11, integrate both performance changes into the same EXE and backup transaction. `PERFORMANCE_OK` confirms the optimized output fingerprint.
4. A terminal prompt showing **`INSTALL_OK`** indicates a successful installation.

### Step 3: Recommended Configuration
1. Launch the patched `Magpie.exe`.
2. Create or edit a configuration item in the **Effect Profiles**:
   * Add filter: `DLSSNR_AI_Filter`
   * **Input Scaling**: Set both width and height to **25%** (pixel count is 6.25% of the original, balancing the TU116 processing load)
   * **Optical Flow Setting**: Keep optical flow **disabled** during initial testing
   * **Framerate Target**: Set the content framerate to a lower target (e.g., 10–15 FPS) under "Frame Rate & Refresh Rate" for initial tests
3. After activating scaling, confirm the initialization status in the logs and attempt to switch between the three filter styles.

---

## 4. Maintenance & Verification Tools

* **Integrity Check (`Check-30HX.cmd`)**: Double-click to run and select the target `Magpie.exe`; the script will compare the SHA256 of the main program, local core, loader, and model, and verify the integrity of the backup.
* **One-Click Restore (`Restore-30HX.cmd`)**: Run this script after exiting Magpie; it restores original files and removes only files that this installation added. The original `Magpie.exe` is recovered from `.30hx-compat-backup-v1`, undoing both compatibility and performance changes. Existing files and personal settings are preserved; backup files are retained in an archive directory.

---

## 5. Source Code & Local Compilation

The repository provides the complete source code for the injection engine and loader for auditing and secondary building:

```text
Magpie-30HX-Compat/
├── src/
│   ├── PatchEngine.cs         # C# PE injection, section expansion, and hash patching engine
│   ├── PerformancePatch.cs    # Hash-locked 0.6.11 performance changes
│   ├── optimized-downsample.hlsl # Exact embedded shader resource
│   ├── 30hx-performance.patch # Source diff against the upstream 0.6.11 commit
│   ├── LICENSE-Magpie.txt     # Upstream license for the source diff
│   └── pinned_loader.cpp      # Fixed hash core loading proxy based on Windows BCrypt
├── Install.ps1                # Automated installation and retrieval logic
├── versions.json              # Fingerprint manifest for supported EXEs, cores, and models
├── CHECKSUMS.json             # Hash manifest for the patch package itself
├── Install-30HX.cmd           # One-click installation entry
├── Check-30HX.cmd             # One-click verification entry
└── Restore-30HX.cmd           # One-click uninstall and restore entry
```

---

## 6. Contributors and Acknowledgments

### Contributors

* **[Rikuzz67](https://github.com/Rikuzz67)**: Project maintainer, CMP 30HX testing and release coordination.
* **OpenAI Codex (AI coding assistant)**: Assisted with performance code changes, unified installer integration, validation tooling and documentation.
* **Anthropic Claude (AI coding assistant)**: Assisted with project development.

### Acknowledgments

This project stands on the shoulders of giants. Special thanks to:
* **[Blinue/Magpie](https://github.com/Blinue/Magpie)**: For creating the incredible Magpie scaling utility.
* **[SAOG0721/Magpie](https://github.com/SAOG0721/Magpie)**: For maintaining the experimental community fork and integrating the D3D12 DLSSNR pipeline.
* **DLSS5 Modding Community**: For the original models and technical inspiration that made this compatibility patch possible.

---

## 7. Publishing This Patch

The existing repository is [Rikuzz67/CMP30HX_Magpie_patch](https://github.com/Rikuzz67/CMP30HX_Magpie_patch). Publish the patch source and a small patch-only release archive; users download the corresponding community Magpie release and NVIDIA dependencies separately.

### Update the source repository

Open this local repository in GitHub Desktop and review Changes. Include the updated installer, version fingerprints, source code, shader, tests, checksum manifest, build tool and `.gitattributes`. Keep the `src/`, `tests/` and `tools/` directory structure. The exact shader bytes are fingerprinted; `.gitattributes` prevents Windows Git from changing them through automatic line-ending conversion.

Commit with a message such as `Integrate 0.6.11 performance changes into the 30HX installer`, then click **Push origin**. The existing self-compiled `30HXCoreLoader.dll` is unchanged and does not need rebuilding. Python is only needed for the maintainer's release builder and integration tests, not for users installing the patch.

Alternatively, run these commands in PowerShell from the local repository directory:

```powershell
git add -- .gitattributes .gitignore Install.ps1 README.md versions.json CHECKSUMS.json VALIDATION.json VALIDATION-v2.json src tests tools
git commit -m "Integrate v2 performance optimizations into the 30HX community patch"
git push origin main
```

The `dist/` release attachments are excluded from source commits. The AI assistant entries above credit their assistance in this README; GitHub's automatic Contributors graph is based on account-linked commits.

### Create a GitHub release

1. Open **Releases → Draft a new release** in the repository.
2. Choose an unused tag, such as `v2-0.6.10-0.6.11`, targeting the commit just pushed.
3. Use a title such as `30HX community patch v2 — integrated 0.6.11 optimizations`.
4. Attach `Magpie-30HX-Compat-v2-community-0.6.10-0.6.11.zip` and its `.zip.sha256` file.
5. Review the release description below and publish when ready.

Do not include the full Magpie application, NVIDIA model/core DLLs, personal configuration, caches or private logs. The archive contains only an explicit list of patch files, including this project's self-compiled loader. `CHECKSUMS.json` records the packaged file hashes.

Suggested release description:

```text
Supports only the exact SHA256-locked SAOG0721/Magpie community releases 0.6.10 and 0.6.11. This does not support the official Blinue build or unknown recompilations.

0.6.11: One installer now combines 30HX compatibility changes, exact 4:1 Lanczos2 downsample optimization and maximum present queue depth 1. Requires the community 310.9.16 model and the previously validated 616.92 NGX core.
0.6.10: Existing compatibility support is retained; the two performance changes are not applied.

New users: Extract the matching community Magpie release and run Install-30HX.cmd from this patch.
Existing compatibility-patch users: Exit Magpie, run this package's Restore-30HX.cmd, then Install-30HX.cmd.
Use the matching Check/Restore CMD files for maintenance. No Python required. Personal configuration, style and intensity settings are unchanged.

The measured 1080p-to-480x270 downsample saving is about 0.009 ms, approximately 6.5% of that filter stage, not the entire frame. Full-size input does not use this fast path. The queue change limits waiting on the affected presentation path; full-load end-to-end latency has not been quantified.
The neural model remains the main bottleneck. This update does not achieve 1080p 60 FPS with neural processing of every frame or claim full DLSS5 equivalence. All three existing styles and model weights are unchanged.

Install/check/restore, reinstall, and compatibility-only migration tests passed on isolated copies under Windows PowerShell 5.1. Installer validation did not rerun the full continuous application pipeline. See README.md and VALIDATION-v2.json.
```

### Rebuild the release archive

From the repository directory, run:

```powershell
python tools/build_release.py --output-dir dist
```

The tool updates `CHECKSUMS.json`, then creates the ZIP and SHA256 attachment from an explicit file allowlist. It does not access the network or collect unrelated files. Choose an empty output directory for each build; existing release attachments are not overwritten.
