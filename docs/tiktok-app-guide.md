# Guide : créer l'app TikTok pour l'upload des condensés (one-shot, ~15 min)

À faire une seule fois, avec ton compte TikTok **@aileri24**. La validation par
TikTok peut prendre quelques jours — lance-la tôt.

## Étapes

1. **Compte développeur** : va sur <https://developers.tiktok.com>, connecte-toi
   avec ton compte TikTok, accepte les conditions développeur.
2. **Créer l'app** : « Manage apps » → « Connect an app ». Nom : `Replayo`
   (ou ce que tu veux), catégorie : Entertainment, description courte du style
   « Envoi de mes propres condensés de gameplay en brouillon sur mon compte ».
3. **Ajouter le produit « Content Posting API »** dans l'app, et coche le scope
   **`video.upload`** (c'est lui qui permet l'envoi en *inbox/brouillon* —
   pas besoin de `video.publish` ni de l'audit complet « Direct Post »).
4. **Ajouter le produit « Login Kit »** (nécessaire pour l'OAuth) avec comme
   Redirect URI : `http://127.0.0.1:8765/callback/`
5. **Soumettre l'app en review** si demandé. Pour un usage personnel
   (Sandbox/target users), tu peux ajouter ton propre compte @aileri24 comme
   « target user » et utiliser l'app en sandbox sans review complète.
6. **Récupérer les identifiants** : dans l'app → « Credentials » : note le
   **Client Key** et le **Client Secret**.
7. **Les donner à Claude** (ou les mettre toi-même) dans
   `%AppData%\Replayo\tiktok.json` :

   ```json
   { "ClientKey": "xxxx", "ClientSecret": "xxxx" }
   ```

8. Quand le Plan D sera implémenté : lancer `Replayo.exe --tiktok-login` une
   fois — le navigateur s'ouvre, tu autorises, c'est fini pour toujours
   (les tokens se rafraîchissent tout seuls).

## Important

- Ne partage jamais le Client Secret (il reste sur ta machine, hors git).
- L'upload automatique ne sera activé qu'une fois le **Plan C** (audio du jeu
  isolé) livré : tes condensés actuels contiennent Spotify/Discord — ne les
  publie pas tels quels si de la musique tournait pendant la game.
