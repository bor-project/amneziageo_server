# {{ page.title }}

The "Certificates" tab sets the certificate under which the panel answers over HTTPS. With it the TCP services ports of the interfaces and the subscriptions on the port of the panel answer over TLS too. Without a certificate the panel answers in plain HTTP, and the services ports under a certificate the panel makes itself at the start.

{% include shot.html name="certificates" alt="Certificates tab: the domain of the certificate and the paths of the certificate and its key" caption="The panel without a certificate of its own." %}

| Field | What it sets |
|---|---|
| Certificate domain | a directory from `/etc/letsencrypt/live`: choosing a domain fills the two paths below with its `fullchain.pem` and `privkey.pem`; "No certificate" takes the certificate off; "A certificate of its own" opens the fields for your own files |
| Certificate path | the chain in PEM |
| Certificate key path | the private key of this chain, taken together with it |

Saving reads both files and refuses one that does not load, with a code that names the file; the certificate takes effect after "Restart". The directory of the domains is set by `Web__CertificateRoot`, `/etc/letsencrypt/live` by default. The file is read again whenever it changes on the disk, so a renewed certificate is taken without a restart. A certificate missing from the disk at the start stops the server.{% if page.kind == "docker" %} The certificate is read inside the container, so its directory must be in `volumes`: `/etc/letsencrypt:/etc/letsencrypt:ro`; the menu adds this line itself.{% endif %}

## Let's Encrypt

A certificate for the name of the server is got by item 22 of the console [menu](../menu.html) `SSL Certificate Management`: it checks that the name resolves and TCP 80 is free, installs `certbot`, opens 80 in ufw, gets a certificate with an RSA 2048 key by the HTTP-01 check, enables the renewal timer `certbot.timer`, puts the certificate on the panel and offers to open the panel on every address of the host. The question about the name of the server at the installation does the same. The files are taken by their paths in `live`, not as copies: at renewal `certbot` updates the links in `live`, and the panel reads the file again by itself. For renewal the name must point at the server and TCP 80 must stay reachable from outside: `certbot` listens on it only during the check.

The RSA key is needed by Android before version 14: the chain of such a certificate goes up to the ISRG Root X1 root. `certbot` 2 issues an ECDSA key by default, with a chain up to ISRG Root X2, which older Android does not have, and the subscription does not download there. The menu issues a certificate of the same name with an ECDSA key again, with RSA (`--force-renewal`); a `certbot` older than 1.10 does not know the `--key-type` key and issues RSA by itself. By hand: `certbot certonly --standalone --preferred-challenges http --key-type rsa --rsa-key-size 2048 --cert-name <name> -d <name>`.

The "Services" card on the [Overview](overview.html#services) shows each certificate file with its end date and warns 14 days ahead: "Runs out in N d.".

The same settings in the environment of the panel: `Web__Certificate` and `Web__CertificateKey`; they hold until the panel has written its own path, see [First start](server.html#first-start).

## Refusals

| Message | What stands behind it |
|---|---|
| The certificate takes a chain and a key by their paths | one path of the two is set |
| The folder of the certificate file does not exist; The certificate file is not found; The certificate path names a folder, not a file; The panel has no access to the certificate file; The certificate file does not read; The certificate file is empty; The file named holds no valid certificate | the chain |
| The folder of the key file does not exist; The key file is not found; The key path names a folder, not a file; The panel has no access to the key file; The key file does not read; The key file is empty; The file named holds no valid private key; The private key is locked by a password; The private key does not match the certificate | the key |
