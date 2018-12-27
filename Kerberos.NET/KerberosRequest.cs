using Kerberos.NET.Crypto;
using Kerberos.NET.Entities;
using System.Linq;
using System;
using System.Collections.Generic;
using Kerberos.NET.Crypto.AES;

namespace Kerberos.NET
{
    public class KerberosRequest
    {
        private static readonly Dictionary<EncryptionType, Func<KrbApReq, DecryptedData>> Decryptors
            = new Dictionary<EncryptionType, Func<KrbApReq, DecryptedData>>();

        public static void RegisterDecryptor(EncryptionType type, Func<KrbApReq, DecryptedData> func)
        {
            Decryptors[type] = func;
        }

        static KerberosRequest()
        {
            RegisterDecryptor(EncryptionType.RC4_HMAC_NT, (token) => new RC4DecryptedData(token));
            RegisterDecryptor(EncryptionType.RC4_HMAC_NT_EXP, (token) => new RC4DecryptedData(token));

            RegisterDecryptor(EncryptionType.AES128_CTS_HMAC_SHA1_96, (token) => new AES128DecryptedData(token));
            RegisterDecryptor(EncryptionType.AES256_CTS_HMAC_SHA1_96, (token) => new AES256DecryptedData(token));
        }

        public KerberosRequest(byte[] data)
        {
            var element = new Asn1Element(data);

            for (var i = 0; i < element.Count; i++)
            {
                var child = element[i];

                switch (child.Class)
                {
                    case TagClass.Universal:
                        switch (child.UniversalTag)
                        {
                            case MechType.UniversalTag:
                                MechType = new MechType(child.AsString());
                                break;
                        }
                        break;
                    case TagClass.ContextSpecific:
                        switch (child.ContextSpecificTag)
                        {
                            case 0:
                                NegotiationRequest = new NegTokenInit(child[0]);
                                break;
                            case 1:
                                NegotiationResponse = new NegTokenTarg();
                                NegotiationResponse.Decode(child[0]);
                                break;
                            case 110:
                                Request = new KrbApReq(child[0]);
                                break;
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        public MechType MechType { get; }

        public NegTokenInit NegotiationRequest { get; }

        public NegTokenTarg NegotiationResponse { get; }

        public KrbApReq Request { get; }

        public static KerberosRequest Parse(byte[] data)
        {
            var ticket = new KerberosRequest(data);

            return ticket;
        }

        public DecryptedData Decrypt(KeyTable keytab)
        {
            if (NegotiationRequest != null)
            {
                return DecryptNegotiate(NegotiationRequest, keytab);
            }

            if (Request != null)
            {
                return DecryptKerberos(Request, keytab);
            }

            return null;
        }

        private DecryptedData DecryptKerberos(KrbApReq request, KeyTable keytab)
        {
            return Decrypt(request, keytab);
        }

        private static DecryptedData DecryptNegotiate(NegTokenInit negotiationToken, KeyTable keytab)
        {
            var token = negotiationToken?.MechToken?.InnerContextToken;

            return Decrypt(token, keytab);
        }

        private static DecryptedData Decrypt(KrbApReq token, KeyTable keytab)
        {
            if (token?.Ticket?.EncPart == null)
            {
                return null;
            }

            DecryptedData decryptor = null;

            if (Decryptors.TryGetValue(token.Ticket.EncPart.EType, out Func<KrbApReq, DecryptedData> func) && func != null)
            {
                decryptor = func(token);
            }

            if (decryptor != null)
            {
                decryptor.Decrypt(keytab);
            }

            return decryptor;
        }

        public override string ToString()
        {
            var mech = MechType?.Mechanism;
            var messageType = NegotiationRequest?.MechToken?.InnerContextToken?.MessageType;
            var authEType = NegotiationRequest?.MechToken?.InnerContextToken?.Authenticator?.EType;
            var realm = NegotiationRequest?.MechToken?.InnerContextToken?.Ticket?.Realm;
            var ticketEType = NegotiationRequest?.MechToken?.InnerContextToken?.Ticket?.EncPart?.EType;
            var nameType = NegotiationRequest?.MechToken?.InnerContextToken?.Ticket?.SName?.NameType;

            var names = "";
            var snames = NegotiationRequest?.MechToken?.InnerContextToken?.Ticket?.SName?.Names;

            if (snames?.Any() ?? false)
            {
                names = string.Join(",", snames);
            }

            return $"Mechanism: {mech} | MessageType: {messageType} | SName: {nameType}, {names} | Realm: {realm} | Ticket EType: {ticketEType} | Auth EType: {authEType}";
        }
    }
}
