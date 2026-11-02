[<a href="README.md">English</a>]| [<a href="README_ID.md">Bahasa Indonesia</a>]| [<a href="README_PL.md">Polski</a>]

> [!CAUTION]
> ## ⚠️ Waspada Situs Palsu FluentCleaner
>
> **FluentCleaner tidak memiliki afiliasi apa pun dengan `fluentcleaner.org`**
>
> **Satu-satunya sumber resmi** untuk FluentCleaner adalah **repositori GitHub ini**. Saya tidak memiliki, mengoperasikan, atau mendukung situs tersebut maupun situs download pihak ketiga lainnya.
>
> **Demi keamanan Anda, unduh FluentCleaner hanya dari rilis resmi yang dipublikasikan di sini di GitHub.**
>
> Tampilan situs yang rapi bukan jaminan resmi, **selalu verifikasi sumbernya. 🛡️**
---

# FluentCleaner
### modern, transparan, tanpa spyware, tanpa scareware, tanpa dark pattern, tanpa sampah upsell, tanpa trik sulap registry palsu


<img width="1536" height="1024" alt="FluentCleaner" src="FluentCleaner/Assets/Banner.png" />


_i built my own take on a cleaner, inspired by the old CCleaner from back in the 2006 days, just adapted to how things should work today. modern (built with WinUI 3), minimal and focused on actually cleaning what matters (without all the usual nonsense)_
 
i built this because at some point you start noticing a pattern

things that were genuinely good… slowly become worse.
small devs ship something great, a company buys it, optimizes it into oblivion, and suddenly you're left wondering how a simple tool turned into a "what happened here?" story. Ccleaner is basically a case study at this point, everyone knows, nobody needs another paragraph about it

funny enough, CrapCleaner only ever really survived because of the community around it, especially things like the [winapp2.ini](https://github.com/moscadotto/winapp2) signatures. that ecosystem did more for the tool than most official decisions ever did.

i was too lazy to rebuild all cleaners natively, so i just wrote a parser for that format instead. turns out its fast. like… surprisingly fast. faster than what i remember from the old piriform implementation (no idea why that was so slow, proprietary formats, overengineering, or just history doing its thing. doesnt matter anymore anyway)

the UI is built in WinUI3 you know, Microsofts "beautiful but slow" framework, except somehow it still manages to outperform the original. go figure

companies today dont really compete on making things better. they compete on who can add more noise without breaking everything completely. and somewhere along the way, "good tools" just turned into "things people remember fondly"

CCleaner used to be great. now it’s mostly a warning.

anyway, im not trying to fix the indutry. just wanted something that doesnt suck. i'll probably get bored, or it'll evolve into something else, and we end up back at square one like always.

for now i just called it **FluentCleaner**.

it wasnt even meant to be public, but a lot of genuinely nice people asked me to release it, so i probably will
here's a first preview so you can get a feel for the direction. i might end up funding it through donations, we'll see.

if you like it, cool. if not, also fair

## 🚀 Unduh Versi Stabil Terbaru
FluentCleaner hadir dalam dua varian. Di balik layar, mesin pembersih dan parser winapp2.ini-nya sama persis, yang membedakan hanyalah UI dan kebutuhan runtime-nya.

|  | **FluentCleaner** (WinUI 3) | **FluentCleaner Classic** |
|---|---|---|
| Framework | .NET 10 + Windows App SDK | .NET Framework 4.8 (WinForms) |
| Deployment | self-contained (runtime sudah disertakan) | framework-dependent (memakai runtime yang sudah ada di sistem) |
| Ukuran setelah diekstrak | ~140 MB | ~3.57 MB |
| File | 243 | 20 |
| Platform | x64 / ARM64, Windows 10 build 17763+ | hampir semua versi Windows |
| Mesin bersama | `FluentCleaner.Core` (netstandard2.0) — logika pemindaian/pembersihan, parser Winapp2.ini | sama |
| Persyaratan | Windows 10 2004 (Build 19041) atau lebih baru + [runtime Windows App SDK 2.0.1](https://aka.ms/windowsappsdk/2.0/2.0.1/windowsappruntimeinstall-x64.exe) (diinstal sekali, terpisah) | tidak ada — memakai versi .NET Framework apa pun yang sudah ada di sistem Anda |
| Unduh | [⬇ Terbaru](https://github.com/builtbybel/FluentCleaner/releases/latest/download/FluentCleaner-win-x64.zip) | [⬇ Terbaru](https://github.com/builtbybel/FluentCleaner/releases/latest/download/FluentCleaner-Classic-net48.zip) · [info selengkapnya](https://github.com/builtbybel/FluentCleaner/releases/tag/classic-1.0.0) |

**Bingung mau pilih yang mana?** Kalau Anda ingin tampilan modern dan tidak keberatan menginstal Windows App SDK sekali saja, pilih versi utama. Kalau Anda ingin yang sangat kecil, portable, dan framework-dependent (atau Anda memakai komputer lama/terkunci perizinannya), pilih Classic.

### Instal dengan WinGet

Edisi modern **WinUI 3** juga tersedia melalui Windows Package Manager:

```powershell
winget install --id Builtbybel.FluentCleaner --exact
```

Sudah terinstal? Perbarui ke rilis terbaru dengan:
```powershell
winget upgrade --id Builtbybel.FluentCleaner --exact
```
_Saat ini WinGet menginstal edisi modern. FluentCleaner Classic tetap tersedia sebagai unduhan portable._

> 💬 **Classic atau Modern?** [Beri suara di sini](https://github.com/builtbybel/FluentCleaner/discussions/100) — penasaran mana yang akhirnya beneran dipakai orang sehari-hari.

## ❤️ Dukungan

tidak ada perusahaan di balik ini, tanpa investor, tanpa departemen pemasaran. cuma satu orang yang bikin alat karena alternatif yang ada makin tahun makin bobrok. setiap bentuk dukungan sangat berarti untuk menjaga proyek ini tetap jalan

| PayPal | Ko-fi |
|---|---|
| [Donasi lewat PayPal](https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN) | [Dukung di Ko-fi](https://ko-fi.com/builtbybel) |

awal yang kecil, persis seperti CCleaner di masa awalnya

## FAQ

<details>
<summary>apakah ini bakal bikin PC saya lebih cepat?</summary>

sejujurnya? tergantung, dan ini bukan jawaban mengelak

di sistem modern dengan ruang disk kosong yang melimpah, kemungkinan besar Anda tidak akan merasakan peningkatan kecepatan yang drastis.
tapi Microsoft sendiri menyatakan bahwa kehabisan ruang penyimpanan bisa memperlambat sistem dan bahkan memblokir 
update Windows ([sumber](https://support.microsoft.com/en-us/windows/free-up-drive-space-in-windows-85529ccb-c365-490d-b548-831022bc9b32)) jadi kalau drive Anda mulai penuh, bersih-bersih jauh lebih penting dari yang Anda kira.

di luar masalah kecepatan, ada alasan kuat kenapa harus rutin bersih-bersih:
- membebaskan ruang disk yang diam-diam habis terpakai selama berbulan-bulan
- mengatasi masalah aplikasi akibat cache yang korup
- memperkecil ukuran backup
- privasi: data browser, daftar riwayat file yang baru dibuka, sisa-sisa jejak dari aplikasi yang sudah di-uninstall
- menjaga update Windows tetap berjalan lancar
- atau sekadar karena sistem yang rapi terasa lebih nyaman. itu juga alasan yang valid.

Microsoft menyarankan melakukan ini setiap bulan. Storage Sense melakukannya secara otomatis.
FluentCleaner cuma memberi Anda kendali lebih besar atas apa saja yang sebenarnya dibersihkan

</details>

<details>
<summary>FluentCleaner (versi Modern/WinUI) menampilkan "Unknown Hard Error" atau tidak mau jalan</summary>

versi modern membutuhkan [runtime Windows App SDK 2.0.1 x64](https://aka.ms/windowsappsdk/2.0/2.0.1/windowsappruntimeinstall-x64.exe) yang tepat.
versi runtime Windows App SDK yang lebih lama atau lebih baru tidak akan bisa jalan.

setelah menginstalnya, restart Windows agar runtime bisa terdaftar dengan benar.
kalau masalah berlanjut, sertakan pesan error persisnya dan versi Windows Anda ke [issue #85](https://github.com/builtbybel/FluentCleaner/issues/85).

</details>

<details>
<summary>apa sih sebenarnya winapp2.ini itu?</summary>

database aturan pembersihan untuk aplikasi Windows yang dikelola oleh komunitas,
ribuan entri yang dibangun selama lebih dari 15 tahun. database ini memberi tahu FluentCleaner secara presisi
apa saja yang harus dibersihkan untuk setiap aplikasi: folder temp mana saja, path cache mana saja, kunci registry mana saja.
tanpa tebak-tebakan, tanpa wildcard liar yang menyapu seluruh drive Anda.
setiap entri bersifat spesifik, dapat diperiksa, dan dapat diaudit. itulah poin utamanya.

</details>

<details>

<summary>apa yang dimaksud dengan flavor?</summary>

winapp2.ini hadir dalam beberapa varian berbeda tergantung alat apa yang Anda pakai.
FluentCleaner menggunakan flavor CCleaner asli, varian yang sama persis seperti yang dulu menenagai
aplikasi tersebut di masa ketika ia masih layak digunakan

</details>

<details>
<summary>apakah aman?</summary>

keamanannya tergantung apa yang Anda aktifkan. tidak ada yang dijalankan tanpa Anda pilih terlebih dahulu.
entri winapp2.ini hanya menargetkan apa yang secara eksplisit diperintahkan untuk ditargetkan,
tanpa omong kosong menyapu bersih "hapus semua yang ada di temp".
meski begitu: aplikasi ini menghapus file. buat backup kalau ada yang terasa penting

</details>

<details>
<summary>kenapa WinUI 3?</summary>

karena ini tahun 2026 dan aplikasi Windows seharusnya tidak kelihatan seperti dibuat di era 2009.
lagipula kata fluent secara harfiah sudah ada di namanya. rasanya pas saja

</details>

<details>
<summary>CCleaner 7 mencabut dukungan winapp2.ini, apa dampaknya bagi FluentCleaner?</summary>

tidak ada dampaknya. FluentCleaner punya parser sendiri, sepenuhnya independen dari CCleaner
langkah CCleaner mencabut dukungan itu sejujurnya justru jadi bagian dari motivasi saya membangun proyek ini

</details>

<details>

<summary>apakah saya bisa menerjemahkan FluentCleaner ke bahasa saya?</summary>

tentu saja boleh 🙌 aplikasi ini memang dirancang untuk itu. berikut seluruh alurnya:

1. salin `FluentCleaner/Strings/en-US/Resources.resw`
2. buat `FluentCleaner/Strings/{kode-bahasa-anda}/Resources.resw` (contoh: `fr-FR`, `pt-BR`, `zh-CN`)
3. terjemahkan setiap `<value>` — **biarkan nama key (`name="..."`) tidak tersentuh**
4. atur `LblTranslatorCredit` ke nama Anda (tambahkan situs web Anda jika ingin kredit dicantumkan)
5. buka pull request 🎉

dua hal yang sering bikin orang tersandung:
- **jangan utak-atik struktur XML** — jangan mengedit `<data>`, `<resheader>`, header versi, maupun baris `<?xml ...>`. hanya teks di dalam `<value>` yang diterjemahkan.
- **ini hanya untuk UI aplikasi.** database pembersih (winapp2.ini dll.) berasal dari proyek upstream Winapp2 dan tidak diterjemahkan di sini.

simpan berkas sebagai UTF-8 dan Anda siap meluncur. belum melihat bahasa Anda? itu artinya belum ada yang mengerjakannya — bisa jadi giliran Anda 😉

</details>

<details>
<summary>apakah saya bisa menerjemahkan FluentCleaner Classic ke bahasa saya?</summary>

tentu saja bisa 🙌 Classic memuat terjemahannya dari berkas JSON terpisah saat aplikasi berjalan, jadi tidak perlu kompilasi ulang.

1. salin `FluentCleaner.Classic/Localization/en.json`
2. ubah namanya ke locale Anda (contoh: `fr-FR.json`, `pt-BR.json`, `ja-JP.json`)
3. terjemahkan nilai string-nya — **pertahankan setiap key dan placeholder seperti `{0}` tanpa perubahan**
4. isi `_Meta_LanguageDisplayName`, `_Meta_TranslatorName`, dan jika berkenan `_Meta_TranslatorWebsite`
5. uji langsung dari menu **Options > Settings > Open localization folder**
6. kirimkan berkas JSON melalui pull request atau GitHub issue 🎉

lihat [panduan penerjemahan Classic](FluentCleaner.Classic/TRANSLATING_CLASSIC.md) selengkapnya untuk detail lebih lanjut.

</details>

<details>
<summary>apakah saya bisa memakai database winapp2 kustom?</summary>

bisa. FluentCleaner tidak terkunci pada satu sumber saja.

aplikasi seperti BleachBit (aslinya cleaner untuk Linux, saya baru tahu aplikasi ini justru lewat proyek ini,
tapi UI-nya jelek sekali sampai langsung saya buang), dan aplikasi lainnya punya flavor winapp2.ini sendiri,
versi yang dimodifikasi sedikit sesuai kebutuhan spesifik mereka. Anda bisa mengambil salah satu dari mereka
(atau buat sendiri) dan langsung pasang ke FluentCleaner.

cukup letakkan filenya di mana saja di komputer Anda, lalu buka:
**Settings > Database > Custom** dan arahkan ke file tersebut. itu saja.

database resmi dari proyek winapp2 ada di sini:
https://github.com/MoscaDotTo/Winapp2, dikelola oleh komunitas,
diperbarui secara rutin, dan mencakup ribuan aplikasi. Titik awal yang solid
kalau Anda menginginkan cakupan yang lebih luas daripada bawaannya.

</details>

<details>
<summary>di mana saya bisa memantau perkembangan proyek ini?</summary>

saya sering membagikan info insider, build awal, dan sesekali keluh kesah soal winui di **[x/twitter](https://x.com/builtbybel)**. Kalau Anda ingin tahu fitur baru apa yang bakal hadir sebelum dirilis, di sanalah tempatnya.

isu dan permintaan fitur silakan sampaikan di sini di github seperti biasa.

</details>

<a id="task-scheduler"></a>
<details>
<summary>Bisakah saya menjalankan FluentCleaner tanpa UI / dari Task Scheduler?</summary>
bisa.

```powershell
FluentCleaner.exe /AUTO
```

Menjalankan pembersihan senyap memakai pilihan yang saat ini Anda simpan dan langsung keluar setelah selesai.  
Tanpa jendela, konfirmasi, atau interaksi.

```powershell
FluentCleaner.exe /AUTO /SHUTDOWN
```

Perilaku yang sama, tetapi mematikan (shutdown) Windows setelah pembersihan selesai.  
`/SHUTDOWN` sendirian tidak akan melakukan apa-apa.

### Logging

Setiap eksekusi otomatis menambahkan log mendetail ke:

```txt
%AppData%\FluentCleaner\auto.log
```

Log tersebut memuat:
- timestamp
- setiap path yang dihapus, dikelompokkan berdasarkan entri
- total ukuran yang dibersihkan

### Penjadwalan

Untuk mengotomatisasi pembersihan:

1. Buka **Windows Task Scheduler**
2. Buat task baru
3. Tambahkan `FluentCleaner.exe`
4. Gunakan `/AUTO` sebagai argumen

Tidak butuh UI scheduler bawaan di aplikasi.

</details>

<details>
<summary>versi Windows mana saja yang didukung?</summary>

FluentCleaner secara resmi mendukung:

- Windows 10 2004 (Build 19041) dan yang lebih baru
- Windows 11

Tidak wajib pakai Windows 11.
Meskipun menggunakan WinUI 3, aplikasi ini sengaja dibuat agar tetap kompatibel dengan sistem Windows 10 modern juga

</details>

<details>
<summary>bisakah saya mendukung pengembangannya?</summary>

bisa, kalau Anda mau 😄

FluentCleaner adalah proyek satu orang, bukan perusahaan bernilai jutaan dolar dengan investor dan departemen pemasaran.

Kalau Anda ingin mendukung pengembangannya secara finansial, Anda bisa melakukannya di sini:
[PayPal](https://www.paypal.com/donate/?hosted_button_id=99X8UQJQP96WN)


</details>

 ## Mitos Seputar Optimizer
 
<details>
<summary>kenapa FluentCleaner tidak punya fitur X?</summary>

<details>
<summary>penghapusan file aman (dod 7-pass, gutmann 35-pass…)</summary>

jawaban singkat: kelihatannya saja mengesankan, tapi tidak ada gunanya sama sekali.

penimpaan aman (secure overwrite) masuk akal di era 90-an waktu HDD masih jadi standar dan pemulihan data forensik benar-benar jadi ancaman nyata. hari ini:

- **SSD** menggunakan wear leveling dan TRIM. kontroler SSD yang menentukan di mana bit data mendarat secara fisik, bukan software Anda. Anda bisa menimpa file sampai 35 kali dan kontrolernya tetap bakal menulis ke blok NAND yang berbeda-beda. Gutmann sendiri mencatat hal ini di adendum makalahnya sendiri.
- **file yang dihapus FluentCleaner** adalah cache browser, file temp, dan entri log. kalau sampai ada orang yang melakukan pemulihan forensik cuma demi cache Discord Anda, masalah Anda jauh lebih besar daripada sekadar metode penghapusan di cleaner Anda.

penghapusan file normal adalah cara yang tepat di sini. selain itu hanyalah security theater (gimmick keamanan semu).

</details>

<details>
<summary>pembersih registry</summary>

sengaja dihilangkan, dan ini perlu dijelaskan.

premisnya kedengaran masuk akal; kunci yang tidak terpakai (orphaned keys) menumpuk, Windows jadi lambat, lalu dibersihkan bisa membantu. dalam praktiknya:

- Windows memuat registry key secara on-demand (hanya saat dibutuhkan). Sepuluh ribu entri sisa uninstaller sama sekali tidak punya dampak terukur terhadap waktu boot maupun performa. Hal ini sudah dibenchmark berulang-ulang sampai tuntas.
- risiko versus manfaatnya benar-benar terbalik. registry cleaner yang salah menghapus satu key saja bisa merusak aplikasi, atau pada skenario ekstrem merusak OS itu sendiri. keuntungannya cuma plasebo. kerugiannya adalah instalasi Windows yang rusak.

CCleaner punya fitur ini karena itu nilai jual yang *kedengarannya* teknis. FluentCleaner tidak menyediakannya karena merilis fitur yang tujuannya cuma biar kelihatan keren alih-alih memberi manfaat nyata adalah tindakan yang tidak jujur.

kalau Anda memang benar-benar perlu membersihkan sisa-sisa setelah uninstaller yang rusak; [autoruns](https://learn.microsoft.com/en-us/sysinternals/downloads/autoruns) atau edit manual yang tertarget adalah alat yang tepat, bukan alat pembersih massal (bulk cleaner).

</details>


<details>
<summary>filosofi umum</summary>

FluentCleaner menargetkan hal-hal yang jelas-jelas sampah; file cache, data temp, sisa-sisa log. Aplikasi ini sengaja menghindari penambahan fitur berlebih (feature creep) yang mengubah CCleaner dari utilitas yang terfokus menjadi bloatware dengan tawaran upsell VPN setiap kali aplikasi dibuka.



lebih sedikit fitur. fitur yang jujur.

</details>

</details>
