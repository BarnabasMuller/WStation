# K0 probe (WStation 3.0)

Checks whether .NET 10 (self-contained) runs on the target NAS and what it can do: HTTPS, time zone,
file locking, atomic replace, a 20 MB CSV, PBKDF2 speed, AES-GCM/ECDSA, and Kestrel HTTPS for 60 s.
See appendix B of [docs/dev/PLAN-3.0.md](../../docs/dev/PLAN-3.0.md).

Build (both packages):

```
dotnet publish -c Release -r linux-arm64      -o out/k0/glibc
dotnet publish -c Release -r linux-musl-arm64 -o out/k0/musl
```

Copy `proba.sh` (LF line endings) next to the `glibc` and `musl` folders, upload the folder to the NAS and run
`sh <folder>/proba.sh` from the DSM Task Scheduler as a normal user. Results: `eredmeny_*.txt` in the same folder.
