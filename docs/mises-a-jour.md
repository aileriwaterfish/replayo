# Mises à jour Replayo

L'application est préparée pour vérifier les [Releases GitHub](https://github.com/aileriwaterfish/replayo/releases) au démarrage, puis chaque jour. Elle prévient si une version stable plus récente existe. Le menu de l'icône Replayo contient « Vérifier les mises à jour… ». L'installation exige une confirmation : Replayo télécharge le ZIP, vérifie son SHA-256, ferme proprement la capture et les REC, remplace les fichiers puis redémarre. Les réglages et clips sont hors du dossier de l'application et restent intacts.

**Distribution privée :** `aileriwaterfish/replayo` est privé. Replayo utilise la connexion locale de GitHub CLI (`gh auth login`) sur les PC qui ont accès au dépôt ; aucun jeton n'est stocké dans l'app. Les clients sans accès au dépôt ne peuvent pas recevoir ces Releases. Ne pas annoncer cette version comme une mise à jour automatique fonctionnelle pour les clients. Un canal lié à la licence reste à créer avant leur diffusion.

Les versions antérieures à v0.1.2 n'ont pas de mise à jour fiable avec le dépôt privé : il faut installer v0.1.2 une première fois à la main. Le ZIP source automatique de GitHub n'est pas l'application ; télécharger `Replayo-win-x64.zip` dans la Release.

## Publier une nouvelle version

1. Modifier `<Version>` dans `src/Replayo/Replayo.csproj` et tester.
2. Sur le poste qui possède `tools/ffmpeg/ffmpeg.exe`, exécuter `pwsh -File tools/package-release.ps1 -Version 0.1.2` en remplaçant la version. Le script génère `dist/releases/Replayo-win-x64.zip` et `dist/releases/SHA256SUMS.txt`.
3. Créer le tag `v0.1.2` sur le commit testé, puis une Release GitHub stable avec exactement ces deux fichiers. Ne pas publier une Release avant que le ZIP et le SHA-256 correspondent : les clients refuseraient la mise à jour.

L'exécutable de mise à jour séparé `Replayo.Updater.exe` est copié temporairement dans `%LocalAppData%\Replayo\updates`. En cas d'échec pendant le remplacement, il restaure les anciens fichiers et note l'erreur dans `%LocalAppData%\Replayo\replayo.log`.
