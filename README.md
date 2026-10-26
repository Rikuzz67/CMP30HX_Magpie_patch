# CMP 30HX · Magpie Community Edition DLSSNR One-Click Compatibility Patch

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
* **One-Click Restore (`Restore-30HX.cmd`)**: Run this script after exiting Magpie; it will automatically delete the newly added `30HXCoreLoader.dll` and local core, and completely restore the unpatched `Magpie.exe` from the original backup directory (`.30hx-compat-backup-v1`).

---

## 5. Source Code & Local Compilation

The repository provides the complete source code for the injection engine and loader for auditing and secondary building:

```text
Magpie-30HX-Compat/
├── src/
│   ├── PatchEngine.cs         # C# PE injection, section expansion, and hash patching engine
│   └── pinned_loader.cpp      # Fixed hash core loading proxy based on Windows BCrypt
├── Install.ps1                # Automated installation and retrieval logic
├── versions.json              # Fingerprint manifest for supported EXEs, cores, and models
├── CHECKSUMS.json             # Hash manifest for the patch package itself
├── Install-30HX.cmd           # One-click installation entry
├── Check-30HX.cmd             # One-click verification entry
└── Restore-30HX.cmd           # One-click uninstall and restore entry