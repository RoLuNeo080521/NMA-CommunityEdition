const { Octokit } = require("@octokit/rest");

// Get required environment variables
const token = process.env.GITHUB_TOKEN;
const repo = process.env.GITHUB_REPOSITORY;
const issueNumber = process.env.ISSUE_NUMBER || process.env.GITHUB_REF?.split('/').pop();

if (!token || !repo || !issueNumber) {
  console.error("Missing required environment variables.");
  process.exit(1);
}

const [owner, repoName] = repo.split('/');

const octokit = new Octokit({ auth: token });

async function run() {
  // Fetch the issue
  console.log(`Fetching issue #${issueNumber} from ${owner}/${repoName}...`);
  const { data: issue } = await octokit.issues.get({
    owner,
    repo: repoName,
    issue_number: issueNumber,
  });

  // Parse the form fields from the issue body (simple example)
  // You may want to improve this parsing for more complex forms
  const body = issue.body;
  if (!body.includes("### Supported Game Check")) {
    console.error("This script is intended for issues created with the 'Missing Game' form.");
    process.exit(0);
  }

  // Extract fields using RegExp
  const game = body.match(/### Game\s*\n([^\n]+)/i)?.[1]?.trim() || "N/A";
  const os = body.match(/### Operating System\s*\n([^\n]+)/i)?.[1]?.trim() || "N/A";
  const linuxDistro = body.match(/### Linux Distribution\s*\n([^\n]+)/i)?.[1]?.trim() || "";
  const store = body.match(/### Game Launcher\s*\n([^\n]+)/i)?.[1]?.trim() || "N/A";
  const logs = body.match(/### Attach Log Files\s*\n([\s\S]*)/i)?.[1]?.trim() || "N/A";

  // Build new issue body
  const newBody = `
## 🕹️ Game Not Found

**Game Name:** ${game}
**Operating System:** ${os} ${ os.toLowerCase().includes('linux') ? `${linuxDistro}` : ''}
**Game Store:** ${store === "Other" ? otherStore : store}

---

### Log Files

${logs}
`;

  // Update the issue with the new body
  await octokit.issues.update({
    owner,
    repo: repoName,
    issue_number: issueNumber,
    body: newBody,
    title: `Game Not Found: ${game} (${store}/${os})`,
  });

  console.log("Issue reformatted successfully.");

  // We may want to post a comment based on the responses.
  let commentBody = '';
  if (store === 'Manually Installed') {
    commentBody = 'Games installed outside of a game store are not supported. Please install your game with Steam, Heroic or Lutris (GOG games only), or with GOG Galaxy or the Epic Games Launcher in a Wine prefix.';
  }
  else if (store === 'Heroic Games Launcher (Epic Games)') {
    commentBody = `We currently only support GOG games installed with Heroic. Epic games are found when the Epic Games Launcher installed them in a Wine prefix; Heroic support is on the roadmap: https://github.com/RoLuNeo080521/NMA-CommunityEdition#roadmap`
  }
  else if (['Linux', 'Steam Deck'].includes(os) && store === 'Lutris') {
    commentBody = `We currently only support GOG games installed with Lutris on Linux.`
  }

  if (commentBody.length > 0) {
    await octokit.issues.createComment({
      owner,
      repo: repoName,
      issue_number: issueNumber,
      body: commentBody,
    });
    console.log("Comment posted successfully.");
  } else {
    console.log("No comment needed based on the responses.");
  }

  // If the user has added nonsense to the logs section, automatically flag the issue as such
  // Since 1.2.2 the log header says "NMA Community Edition log file", before it said "Nexus Mods App log file"
  if (!logs.includes('nmacommunity.app.main') && !logs.includes('nmacommunity.app.slim') && !logs.includes('NMA Community Edition log file') && !logs.includes('Nexus Mods App log file')) {
    console.log('Issue doesn\`t seem to contain valid logs, tagging invalid');
    await octokit.issues.addLabels({
        owner,
        repo: repoName,
        issue_number: issueNumber,
        labels: [ 'invalid', 'needs logs' ]
    });
  }
}

run().catch(err => {
  console.error(err);
  process.exit(1);
});
