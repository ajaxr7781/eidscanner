using System.Security.Cryptography.Xml;
using System.Xml;

namespace EidAgent.Services;

internal static class IcaSdkIntegrationHelpers
{
    public static string GenerateRequestId()
    {
        var buffer = new byte[40];
        Random.Shared.NextBytes(buffer);
        return Convert.ToBase64String(buffer);
    }

    public static bool CompareRequestId(string expectedRequestId, string responseXml)
    {
        using var xmlReader = XmlReader.Create(new StringReader(responseXml));
        xmlReader.ReadToFollowing("RequestID");
        var responseRequestId = xmlReader.ReadElementContentAsString();
        return string.Equals(expectedRequestId, responseRequestId, StringComparison.Ordinal);
    }

    public static bool VerifySignature(string responseXml)
    {
        var xmlDocument = new XmlDocument { PreserveWhitespace = false };
        xmlDocument.LoadXml(responseXml);

        var signedXmlWithId = new SignedXmlWithId(xmlDocument);
        var signatureNodes = xmlDocument.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");

        if (signatureNodes.Count == 0)
        {
            return false;
        }

        signedXmlWithId.LoadXml((XmlElement)signatureNodes[0]!);
        return signedXmlWithId.CheckSignature();
    }
}

internal sealed class SignedXmlWithId : SignedXml
{
    public SignedXmlWithId(XmlDocument xmlDocument)
        : base(xmlDocument)
    {
    }

    public override XmlElement? GetIdElement(XmlDocument? document, string idValue)
    {
        if (document is null)
        {
            return null;
        }

        var element = base.GetIdElement(document, idValue);
        if (element is not null)
        {
            return element;
        }

        var namespaceManager = new XmlNamespaceManager(document.NameTable);
        namespaceManager.AddNamespace("rate", "http://www.emiratesid.ae/vg");
        return document.SelectSingleNode("//rate:Message", namespaceManager) as XmlElement;
    }
}
