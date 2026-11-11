# Security

## Reporting a problem

Please report security problems privately: on this repository, choose **Security > Report a
vulnerability**.  Don't open a public issue for them.  You will get an answer, and a fix will be
released as soon as it can be.

Only the newest version is supported.

## What counts

Anything that could let someone else read or change a person's tree, keys or computer, for example:

- a way round the Assistant's approvals, so that a change happens without the person allowing it;
- a way for an AI model, an MCP server or a web page the Assistant reads to send the tree, keys or
  files somewhere the person didn't choose;
- a way round the per-site permission of `fetch_url`, or the Assistant reaching sites it shouldn't;
- the MCP server for other programs accepting a connection or a tool call it should refuse;
- the update client installing a release that isn't signed with the publisher's key;
- API keys, sign-in tokens or conversations stored or logged unencrypted.

## How Mycelia protects itself

- **Approvals.**  Every Assistant tool that changes the tree, writes a file or reaches the web asks
  first, unless the person chose "Always allow" for it.  The exception is `fetch_url` reading the
  listed archive APIs (`familyTree/Ai/Research/ResearchApis.cs`); any other site asks, and "Always
  allow" there is per site.  Other programs' calls go through the same approvals.
- **Secrets.**  API keys, request headers, environment values, sign-in tokens and conversations
  are encrypted with Windows DPAPI for the signed-in user.  Archive keys are added to requests by
  Mycelia and redacted from anything the model sees.
- **The MCP server** is off by default, listens on this computer only and needs a token.
- **Updates** are checked against the publisher's ECDSA P-256 public key, which is in
  `familyTree/Updates/UpdateService.cs`.  A release signed with any other key is refused.
