using System;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens; 
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using WebApi.Entities;
using WebApi.Helpers;
using WebApi.Models;
using System.Security.Cryptography;

namespace WebApi.Services
{
    public interface IUserService
    {
        AuthenticateResponse Authenticate(AuthenticateRequest model, string ipAddress);
        AuthenticateResponse RefreshToken(string token, string ipAddress);
        bool RevokeToken(string token, string ipAddress);
        IEnumerable<User> GetAll();
        User GetById(int id); 
        bool Register(User model);
    }

    public class UserService : IUserService
    {
        private readonly AppSettings _appSettings;
        private readonly WebApiDbContext _webApiDbcontext;

        public UserService(WebApiDbContext webApiDbcontext,  IOptions<AppSettings> appSettings)
        {
            _webApiDbcontext = webApiDbcontext;
            _appSettings = appSettings.Value;
        }

        public AuthenticateResponse Authenticate(AuthenticateRequest model, string ipAddress)
        {
            var user = _webApiDbcontext.User.SingleOrDefault(x => x.Username == model.Username && x.Password == model.Password);

            // return null if user not found
            if (user == null) return null;

            // authentication successful so generate jwt and refresh tokens
            var jwtToken = GenerateJwtToken(user);
            var refreshToken = GenerateRefreshToken(ipAddress);
            if (user.RefreshTokens == null)
            {
                user.RefreshTokens = new List<RefreshToken>();
            }
            // save refresh token
            user.RefreshTokens.Add(refreshToken);
            _webApiDbcontext.Update(user);
            _webApiDbcontext.SaveChanges();

            return new AuthenticateResponse(user, jwtToken, refreshToken.Token);
        }

        public AuthenticateResponse RefreshToken(string token, string ipAddress)
        {
            var user = _webApiDbcontext.User.SingleOrDefault(u => u.RefreshTokens.Any(t => t.Token == token));

            // return null if no user found with token
            if (user == null) return null;

            var refreshToken = user.RefreshTokens.Single(x => x.Token == token);

            // return null if token is no longer active
            if (!refreshToken.IsActive) return null;

            // replace old refresh token with a new one and save
            var newRefreshToken = GenerateRefreshToken(ipAddress);
            refreshToken.Revoked = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;
            refreshToken.ReplacedByToken = newRefreshToken.Token;
            user.RefreshTokens.Add(newRefreshToken);
            _webApiDbcontext.Update(user);
            _webApiDbcontext.SaveChanges();

            // generate new jwt
            var jwtToken = GenerateJwtToken(user);

            return new AuthenticateResponse(user, jwtToken, newRefreshToken.Token);
        }

        public bool RevokeToken(string token, string ipAddress)
        {
            var user = _webApiDbcontext.User.SingleOrDefault(u => u.RefreshTokens.Any(t => t.Token == token));

            // return false if no user found with token
            if (user == null) return false;

            var refreshToken = user.RefreshTokens.Single(x => x.Token == token);

            // return false if token is not active
            if (!refreshToken.IsActive) return false;

            // revoke token and save
            refreshToken.Revoked = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;
            _webApiDbcontext.Update(user);
            _webApiDbcontext.SaveChanges();

            return true;
        }

        public IEnumerable<User> GetAll()
        {
            return _webApiDbcontext.User;
        }

        public User GetById(int id)
        {
            return _webApiDbcontext.User.Find(id);
        }

        // helper methods

        private string GenerateJwtToken(User user)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(_appSettings.Secret);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim("id", user.Id.ToString())
                }),
                Expires = DateTime.UtcNow.AddMinutes(15),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        private RefreshToken GenerateRefreshToken(string ipAddress)
        {
            using (var rngCryptoServiceProvider = new RNGCryptoServiceProvider())
            {
                var randomBytes = new byte[64];
                rngCryptoServiceProvider.GetBytes(randomBytes);
                return new RefreshToken
                {
                    Token = Convert.ToBase64String(randomBytes),
                    Expires = DateTime.UtcNow.AddDays(7),
                    Created = DateTime.UtcNow,
                    CreatedByIp = ipAddress
                };
            }
        }
        public bool Register(User user)
        {
            bool isRegisted = false;
            using (var transaction = _webApiDbcontext.Database.BeginTransaction())
            {
                try
                {
                    if (user.Id > 0)
                    {
                        User UpdatedUser = GetById(user.Id);
                        UpdatedUser.FirstName = user.FirstName;
                        UpdatedUser.LastName = user.LastName;
                        UpdatedUser.Password = user.Password;
                    }
                    else
                    {
                        user.RefreshTokens = new List<RefreshToken>();
                        _webApiDbcontext.User.Add(user);
                    }
                    _webApiDbcontext.SaveChanges();
                    transaction.Commit();
                    isRegisted = true;
                }
                catch (Exception ex)
                {
                    isRegisted = false;
                }
            }
            return isRegisted;
        }
    }
}
