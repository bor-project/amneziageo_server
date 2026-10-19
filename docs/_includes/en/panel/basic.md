# {{ page.title }}

Two lists stand ahead of all the rules: "Block" drops what fits, "Direct" lets it out through the host without a mark, and no rule after them is read. The block is read before the direct list.

{% include shot.html name="basic" alt="Basic page: the lists Block and Direct" caption="Advertising into the block, the home network and a domain directly." %}

Each list takes up to 1024 targets, written the same way as the targets of a rule: `geoip:ru`, `geosite:category-ads-all`, `203.0.113.0/24`, `example.com`, `keyword:ads`. An empty list gives no line in the set. The lists live in the rule set under the numbers -1 (block) and -2 (direct), and the resolver fills their name sets the same way as the sets of the rules, see [DNS](dns.html).

Like rules, the lists decide on the first packet of a connection: a connection opened before its address got into the block lives until it ends. "Save" puts the lists on the host at once; the status line shows what the host carries for each.

Typical uses: the advertising domains `geosite:category-ads-all` into the block, the home network of the server or local services into "Direct", so that no rule by country takes them into a channel.
